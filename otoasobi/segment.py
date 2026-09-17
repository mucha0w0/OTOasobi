from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from otoasobi.features import FrameTrack, close_short_gaps, runs
from otoasobi.parser import Token, speech_chunks


@dataclass
class Span:
    start: float
    end: float
    label: str
    token: str


def _frame_time(track: FrameTrack, index: int, end: bool = False) -> float:
    if end:
        return min((index * track.hop) / track.sr, track.times[-1] + track.hop / track.sr)
    return max(0.0, (index * track.hop) / track.sr)


def _speech_mask(track: FrameTrack) -> np.ndarray:
    peak = float(np.max(track.rms)) + 1e-12
    noise = float(np.percentile(track.rms, 8))
    thr = max(peak * 0.045, noise * 4.0, 1e-4)
    mask = track.rms > thr
    max_gap = max(1, int(0.09 * track.sr / track.hop))
    return close_short_gaps(mask, max_gap)


def _bounds_from_mask(track: FrameTrack, duration: float) -> tuple[float, float]:
    mask = _speech_mask(track)
    if not np.any(mask):
        return 0.0, duration
    start_i = int(np.argmax(mask))
    end_i = int(len(mask) - 1 - np.argmax(mask[::-1])) + 1
    return _frame_time(track, start_i), min(duration, _frame_time(track, end_i, end=True))


def _fricative_onsets(track: FrameTrack, start: float, end: float, count: int) -> list[float] | None:
    if count <= 0:
        return []
    t0 = start
    t1 = end
    in_span = (track.times >= t0) & (track.times <= t1)
    if not np.any(in_span):
        return None
    rms = track.rms[in_span]
    ratio = track.ratio[in_span]
    times = track.times[in_span]
    speech_thr = max(float(np.max(rms)) * 0.08, 1e-4)
    fric = (ratio > 1.8) & (rms > speech_thr * 0.25)
    islands = runs(fric)
    # merge islands closer than 40ms
    merged: list[tuple[int, int]] = []
    for a, b in islands:
        if merged and times[a] - times[merged[-1][1] - 1] < 0.04:
            merged[-1] = (merged[-1][0], b)
        else:
            merged.append((a, b))
    if len(merged) != count:
        return None
    return [float(times[a]) for a, _ in merged]


def _speech_thr(track: FrameTrack) -> float:
    return max(float(np.max(track.rms)) * 0.08, 1e-4)


def _spaced_peaks(times: np.ndarray, score: np.ndarray, count: int, min_dt: float) -> list[float] | None:
    if count <= 0 or len(times) == 0:
        return [] if count == 0 else None
    order = np.argsort(score)[::-1]
    chosen: list[float] = []
    for idx in order:
        t = float(times[int(idx)])
        if any(abs(t - c) < min_dt for c in chosen):
            continue
        chosen.append(t)
        if len(chosen) == count:
            return sorted(chosen)
    return None


def stop_closure_start(track: FrameTrack, burst: float, t_lo: float, max_closure: float = 0.12) -> float:
    """Start of a stop including the silent closure, without eating a long rest."""
    t_lo = max(0.0, t_lo)
    burst = max(burst, t_lo + 0.01)
    min_t = max(t_lo, burst - max_closure)
    sel = (track.times >= min_t) & (track.times <= burst + 0.01)
    if int(np.count_nonzero(sel)) < 3:
        return min_t
    times = track.times[sel]
    rms = track.rms[sel]
    flux = track.flux[sel]
    peak_i = int(np.argmin(np.abs(times - burst)))
    vowel_thr = max(float(np.max(rms)) * 0.38, _speech_thr(track))
    flux_thr = max(float(np.percentile(flux, 65)), 1e-9)
    i = peak_i
    while i > 0 and flux[i - 1] >= flux_thr * 0.35:
        i -= 1
    while i > 0 and rms[i - 1] < vowel_thr:
        i -= 1
    return max(min_t, float(times[i]))


def nasal_onset(track: FrameTrack, peak: float, t_lo: float, t_hi: float) -> float:
    t_lo = max(0.0, t_lo)
    peak = min(max(peak, t_lo + 0.01), max(t_lo + 0.02, t_hi))
    sel = (track.times >= t_lo) & (track.times <= peak + 0.02)
    if int(np.count_nonzero(sel)) < 3:
        return max(t_lo, peak - 0.06)
    times = track.times[sel]
    centroid = track.centroid[sel]
    rms = track.rms[sel]
    peak_i = int(np.argmin(np.abs(times - peak)))
    speech_thr = _speech_thr(track)
    vowel_cen = max(float(np.median(centroid)), 900.0)
    i = peak_i
    while i > 0 and rms[i - 1] > speech_thr * 0.25 and centroid[i - 1] < vowel_cen:
        i -= 1
    return max(t_lo, float(times[i]))


def stop_vowel_start(track: FrameTrack, start: float, end: float) -> float:
    sel = (track.times >= start) & (track.times <= end)
    if not np.any(sel):
        return start + (end - start) * 0.35
    times = track.times[sel]
    flux = track.flux[sel]
    inner = slice(1, max(2, len(times) - 1))
    times_i = times[inner]
    if len(times_i) == 0:
        return start + 0.03
    burst = float(times_i[int(np.argmax(flux[inner]))])
    cut = burst + 0.018
    return min(max(cut, start + 0.02), end - 0.015)


def nasal_vowel_start(track: FrameTrack, start: float, end: float) -> float:
    sel = (track.times >= start) & (track.times <= end)
    if not np.any(sel):
        return start + (end - start) * 0.32
    times = track.times[sel]
    centroid = track.centroid[sel]
    inner = slice(1, max(2, len(times) - 1))
    times_i = times[inner]
    cen_i = centroid[inner]
    if len(times_i) == 0:
        return start + (end - start) * 0.32
    thr = float(np.median(cen_i))
    vowelish = cen_i > thr
    if np.any(vowelish):
        cut = float(times_i[int(np.argmax(vowelish))])
    else:
        cut = start + (end - start) * 0.32
    return min(max(cut, start + 0.02), end - 0.015)


def _stop_onsets(track: FrameTrack, start: float, end: float, count: int) -> list[float] | None:
    sel = (track.times >= start) & (track.times <= end)
    if not np.any(sel):
        return None
    peaks = _spaced_peaks(track.times[sel], track.flux[sel], count, min_dt=0.11)
    if peaks is None:
        return None
    onsets = [stop_closure_start(track, peak, start if i == 0 else peaks[i - 1], 0.12) for i, peak in enumerate(peaks)]
    onsets[0] = start
    return onsets


def _nasal_onsets(track: FrameTrack, start: float, end: float, count: int) -> list[float] | None:
    sel = (track.times >= start) & (track.times <= end)
    if not np.any(sel):
        return None
    times = track.times[sel]
    inv = -track.centroid[sel]
    peaks = _spaced_peaks(times, inv, count, min_dt=0.10)
    if peaks is None:
        return None
    onsets = [nasal_onset(track, peak, start if i == 0 else peaks[i - 1], end) for i, peak in enumerate(peaks)]
    onsets[0] = start
    return onsets


F2_REF = {"i": 2300.0, "e": 1900.0, "a": 1250.0, "o": 950.0, "u": 850.0}


def _rms_valley(track: FrameTrack, t0: float, t1: float, fallback: float) -> float:
    sel = (track.times >= t0) & (track.times <= t1)
    if int(np.count_nonzero(sel)) < 4:
        return fallback
    times = track.times[sel]
    rms = track.rms[sel]
    win = max(3, min(7, len(rms) // 3 * 2 + 1))
    if win % 2 == 0:
        win += 1
    kernel = np.ones(win) / win
    smooth = np.convolve(rms, kernel, mode="same")
    inner = slice(1, max(2, len(smooth) - 1))
    idx = int(np.argmin(smooth[inner])) + 1
    return float(times[idx])


def _max_delta_time(times: np.ndarray, values: np.ndarray, t0: float, t1: float, fallback: float) -> float:
    sel = (times >= t0) & (times <= t1) & np.isfinite(values)
    if int(np.count_nonzero(sel)) < 4:
        return fallback
    t = times[sel]
    v = values[sel]
    d = np.abs(np.diff(v))
    return float(t[int(np.argmax(d)) + 1])


def _f2_crossing(
    times: np.ndarray,
    f2: np.ndarray,
    t0: float,
    t1: float,
    v0: str | None,
    v1: str | None,
    fallback: float,
) -> float:
    sel = (times >= t0) & (times <= t1) & np.isfinite(f2)
    if int(np.count_nonzero(sel)) < 4:
        return fallback
    t = times[sel]
    v = f2[sel]
    target0 = F2_REF.get(v0 or "", 1200.0)
    target1 = F2_REF.get(v1 or "", 1200.0)
    mid_f = 0.5 * (target0 + target1)
    idx = int(np.argmin(np.abs(v - mid_f)))
    cut = float(t[idx])
    if min(target0, target1) - 80 <= v[idx] <= max(target0, target1) + 80:
        return cut
    return _max_delta_time(times, f2, t0, t1, fallback)


def vowel_boundary(
    track: FrameTrack,
    t_a: float,
    t_b: float,
    v_a: str | None,
    v_b: str | None,
    f2: tuple[np.ndarray, np.ndarray] | None = None,
) -> float:
    t0, t1 = (t_a, t_b) if t_a <= t_b else (t_b, t_a)
    mid = 0.5 * (t0 + t1)
    if t1 - t0 < 0.05:
        return mid
    if v_a == v_b:
        return _rms_valley(track, t0, t1, mid)
    if f2 is not None:
        return _f2_crossing(f2[0], f2[1], t0, t1, v_a, v_b, mid)
    return _max_delta_time(track.times, track.bright, t0, t1, mid)


def spread_peaks(peaks: list[float], t0: float, t1: float, min_sep: float = 0.11) -> list[float]:
    p = [float(x) for x in peaks]
    n = len(p)
    if n <= 1:
        return p
    span = max(t1 - t0, min_sep * n)
    if span < min_sep * (n - 0.5):
        step = span / n
        return [t0 + (i + 0.5) * step for i in range(n)]
    for _ in range(10):
        p[0] = min(max(p[0], t0 + 0.03), t1 - min_sep * (n - 1) - 0.03)
        for i in range(1, n):
            p[i] = max(p[i], p[i - 1] + min_sep)
        p[-1] = min(p[-1], t1 - 0.03)
        for i in range(n - 2, -1, -1):
            p[i] = min(p[i], p[i + 1] - min_sep)
    return p


def vowel_mora_onsets(
    track: FrameTrack,
    start: float,
    end: float,
    vowels: list[str | None],
    peaks: list[float] | None = None,
    f2: tuple[np.ndarray, np.ndarray] | None = None,
) -> list[float]:
    n = len(vowels)
    if n <= 1:
        return [start]
    if peaks is not None and len(peaks) == n:
        peaks = spread_peaks(peaks, start, end)
        onsets = [start]
        for i in range(1, n):
            mid = 0.5 * (peaks[i - 1] + peaks[i])
            acoustic = vowel_boundary(track, peaks[i - 1], peaks[i], vowels[i - 1], vowels[i], f2)
            if abs(acoustic - mid) <= 0.04:
                onsets.append(acoustic)
            else:
                onsets.append(mid)
        for i in range(1, n):
            onsets[i] = max(onsets[i], onsets[i - 1] + 0.07)
            onsets[i] = min(onsets[i], end - 0.07 * (n - i))
        onsets[0] = start
        return onsets
    step = (end - start) / n
    onsets = [start]
    for i in range(1, n):
        guess = start + i * step
        lo = max(start, guess - step * 0.45)
        hi = min(end, guess + step * 0.45)
        onsets.append(vowel_boundary(track, lo, hi, vowels[i - 1], vowels[i], f2))
    for i in range(1, n):
        onsets[i] = max(onsets[i], onsets[i - 1] + 0.07)
        onsets[i] = min(onsets[i], end - 0.07 * (n - i))
    onsets[0] = start
    return onsets


def _equal_bounds(start: float, end: float, count: int) -> list[tuple[float, float]]:
    if count <= 0:
        return []
    step = (end - start) / count
    return [(start + i * step, start + (i + 1) * step) for i in range(count)]


def _onsets_to_bounds(onsets: list[float], start: float, end: float) -> list[tuple[float, float]]:
    bounds = []
    for i, onset in enumerate(onsets):
        nxt = onsets[i + 1] if i + 1 < len(onsets) else end
        bounds.append((onset, nxt))
    bounds[0] = (start, bounds[0][1])
    bounds[-1] = (bounds[-1][0], end)
    return bounds


def _mora_bounds(track: FrameTrack, moras: list[Token], start: float, end: float) -> list[tuple[float, float]]:
    n = len(moras)
    if n == 0:
        return []
    if n == 1:
        return [(start, end)]
    if all(m.is_fricative and m.needs_cv_split for m in moras):
        onsets = _fricative_onsets(track, start, end, n)
        if onsets is not None:
            return _onsets_to_bounds(onsets, start, end)
    if all(m.is_stop and m.needs_cv_split for m in moras):
        onsets = _stop_onsets(track, start, end, n)
        if onsets is not None:
            return _onsets_to_bounds(onsets, start, end)
    if all(m.is_nasal and m.needs_cv_split for m in moras):
        onsets = _nasal_onsets(track, start, end, n)
        if onsets is not None:
            return _onsets_to_bounds(onsets, start, end)
    if all(m.is_vowel_only for m in moras):
        onsets = vowel_mora_onsets(track, start, end, [m.vowel for m in moras])
        return _onsets_to_bounds(onsets, start, end)
    return _equal_bounds(start, end, n)


def _cv_cut(track: FrameTrack, mora: Token, start: float, end: float) -> float:
    if end - start < 0.04:
        return start + (end - start) * 0.35
    if mora.is_fricative:
        sel = (track.times >= start) & (track.times <= end)
        if not np.any(sel):
            return start + (end - start) * 0.3
        times = track.times[sel]
        ratio = track.ratio[sel]
        inner = slice(1, max(2, len(times) - 1))
        times_i = times[inner]
        score = ratio[inner]
        vowelish = score < 1.2
        if np.any(vowelish):
            cut = float(times_i[int(np.argmax(vowelish))])
        else:
            cut = start + (end - start) * 0.28
    elif mora.is_stop:
        cut = stop_vowel_start(track, start, end)
    elif mora.is_nasal:
        cut = nasal_vowel_start(track, start, end)
    else:
        sel = (track.times >= start) & (track.times <= end)
        if not np.any(sel):
            return start + (end - start) * 0.3
        times = track.times[sel]
        zcr = track.zcr[sel]
        inner = slice(1, max(2, len(times) - 1))
        times_i = times[inner]
        score = zcr[inner]
        med = float(np.median(score))
        vowelish = score < med
        cut = float(times_i[int(np.argmax(vowelish))]) if np.any(vowelish) else start + (end - start) * 0.3
    min_c = start + 0.015
    max_c = end - 0.015
    if max_c <= min_c:
        return (start + end) / 2
    return min(max(cut, min_c), max_c)


def _label_mora(mora: Token, start: float, end: float, track: FrameTrack) -> list[Span]:
    if mora.kind in {"N", "Q"}:
        return [Span(start, end, "子音", mora.text)]
    if mora.kind == "V" or not mora.needs_cv_split:
        return [Span(start, end, "母音", mora.text)]
    cut = _cv_cut(track, mora, start, end)
    return [
        Span(start, cut, "子音", mora.text),
        Span(cut, end, "母音", mora.text),
    ]


def segment_tokens(tokens: list[Token], track: FrameTrack, duration: float) -> list[Span]:
    spans: list[Span] = []
    speech_start, speech_end = _bounds_from_mask(track, duration)
    first_speech = next((t for t in tokens if not t.is_rest), None)
    if first_speech is not None and first_speech.is_stop:
        speech_start = stop_closure_start(track, speech_start, 0.0)
    chunks = speech_chunks(tokens)
    leading_rest = bool(tokens) and tokens[0].is_rest
    trailing_rest = bool(tokens) and tokens[-1].is_rest

    if leading_rest:
        rest_end = speech_start
        spans.append(Span(0.0, rest_end, "無音", tokens[0].text))
    else:
        if speech_start > 0.02:
            spans.append(Span(0.0, speech_start, "無音", ""))

    if not chunks:
        if not leading_rest:
            spans.append(Span(0.0, duration, "無音", "_"))
        elif speech_end < duration:
            spans.append(Span(speech_start, duration, "無音", tokens[-1].text if trailing_rest else ""))
        return _clean(spans, duration)

    if len(chunks) == 1:
        chunk_bounds = [(speech_start, speech_end)]
    else:
        # Split the speech region into N chunks using the lowest energy valleys.
        chunk_bounds = _split_speech_chunks(track, speech_start, speech_end, len(chunks))

    rest_tokens = [t for t in tokens if t.is_rest]
    internal_rests = rest_tokens[1:] if leading_rest else rest_tokens
    if trailing_rest and internal_rests:
        internal_rests = internal_rests[:-1]

    for i, moras in enumerate(chunks):
        c0, c1 = chunk_bounds[i]
        mora_bounds = _mora_bounds(track, moras, c0, c1)
        for mora, (m0, m1) in zip(moras, mora_bounds, strict=True):
            spans.extend(_label_mora(mora, m0, m1, track))
        if i < len(chunks) - 1:
            gap_end = chunk_bounds[i + 1][0]
            rest_text = internal_rests[i].text if i < len(internal_rests) else "_"
            if gap_end > c1 + 0.005:
                spans.append(Span(c1, gap_end, "無音", rest_text))

    last_end = spans[-1].end if spans else speech_end
    if last_end < duration - 0.01:
        rest_text = tokens[-1].text if trailing_rest else ""
        spans.append(Span(last_end, duration, "無音", rest_text))
    return _clean(spans, duration)


def _split_speech_chunks(
    track: FrameTrack, start: float, end: float, n_chunks: int
) -> list[tuple[float, float]]:
    if n_chunks <= 1:
        return [(start, end)]
    sel = (track.times >= start) & (track.times <= end)
    times = track.times[sel]
    rms = track.rms[sel]
    if len(times) < n_chunks * 3:
        return _equal_bounds(start, end, n_chunks)
    # moving average to ignore stop closures
    win = max(3, int(0.08 / ((times[1] - times[0]) if len(times) > 1 else 0.01)))
    kernel = np.ones(win) / win
    smooth = np.convolve(rms, kernel, mode="same")
    # pick n_chunks-1 lowest valleys, well separated
    min_sep = max(3, len(times) // (n_chunks * 3))
    order = np.argsort(smooth)
    chosen: list[int] = []
    for idx in order:
        if any(abs(idx - c) < min_sep for c in chosen):
            continue
        if idx < min_sep or idx > len(times) - min_sep:
            continue
        chosen.append(int(idx))
        if len(chosen) == n_chunks - 1:
            break
    if len(chosen) != n_chunks - 1:
        return _equal_bounds(start, end, n_chunks)
    cuts = sorted(float(times[i]) for i in chosen)
    bounds = []
    prev = start
    for cut in cuts:
        bounds.append((prev, cut))
        prev = cut
    bounds.append((prev, end))
    return bounds


def _clean(spans: list[Span], duration: float) -> list[Span]:
    if not spans:
        return [Span(0.0, duration, "無音", "")]
    spans = sorted(spans, key=lambda s: s.start)
    out: list[Span] = []
    for span in spans:
        start = max(0.0, span.start)
        end = min(duration, span.end)
        if end - start < 0.005:
            continue
        if out and abs(span.start - out[-1].end) > 0.0001:
            gap_start = out[-1].end
            if start > gap_start + 0.005:
                out.append(Span(gap_start, start, "無音", ""))
        out.append(Span(start, end, span.label, span.token))
    if out[0].start > 0.005:
        out.insert(0, Span(0.0, out[0].start, "無音", ""))
    else:
        out[0] = Span(0.0, out[0].end, out[0].label, out[0].token)
    if duration - out[-1].end > 0.005:
        out.append(Span(out[-1].end, duration, "無音", ""))
    else:
        last = out[-1]
        out[-1] = Span(last.start, duration, last.label, last.token)
    return out
