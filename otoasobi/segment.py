from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from otoasobi.features import FrameTrack, close_short_gaps, runs
from otoasobi.parser import Token, rest_layout, speech_chunks
from otoasobi.phones import SILENCE_LABEL, phone_name


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


def _rms_threshold(track: FrameTrack) -> float:
    peak = float(np.max(track.rms)) + 1e-12
    noise = float(np.percentile(track.rms, 8))
    return max(peak * 0.045, noise * 4.0, 1e-4)


def _speech_mask(track: FrameTrack, close_ms: float = 30.0) -> np.ndarray:
    mask = track.rms > _rms_threshold(track)
    max_gap = max(1, int(close_ms * 0.001 * track.sr / track.hop))
    return close_short_gaps(mask, max_gap)


def _bounds_from_mask(track: FrameTrack, duration: float) -> tuple[float, float]:
    mask = _speech_mask(track)
    if not np.any(mask):
        return 0.0, duration
    start_i = int(np.argmax(mask))
    end_i = int(len(mask) - 1 - np.argmax(mask[::-1])) + 1
    return _frame_time(track, start_i), min(duration, _frame_time(track, end_i, end=True))


def _islands_from_mask(
    track: FrameTrack, duration: float, mask: np.ndarray, min_dur: float = 0.01
) -> list[tuple[float, float]]:
    out: list[tuple[float, float]] = []
    for a, b in runs(mask):
        t0 = _frame_time(track, a)
        t1 = min(duration, _frame_time(track, b, end=True))
        if t1 - t0 >= min_dur:
            out.append((t0, t1))
    return out


def _split_island_with_gap(track: FrameTrack, start: float, end: float) -> list[tuple[float, float]]:
    """Split one speech island at its lowest valley, keeping a silent gap if present."""
    min_span = 0.05
    if end - start < 0.16:
        mid = 0.5 * (start + end)
        return [(start, mid), (mid, end)]
    sel = (track.times >= start) & (track.times <= end)
    times = track.times[sel]
    rms = track.rms[sel]
    if len(times) < 10:
        mid = 0.5 * (start + end)
        return [(start, mid), (mid, end)]
    dt = float(times[1] - times[0]) if len(times) > 1 else 0.01
    win = max(3, int(0.06 / max(dt, 1e-4)))
    if win % 2 == 0:
        win += 1
    kernel = np.ones(win) / win
    smooth = np.convolve(rms, kernel, mode="same")
    inner_lo = max(2, int(0.12 * len(times)))
    inner_hi = min(len(times) - 3, int(0.88 * len(times)))
    if inner_hi <= inner_lo:
        mid = 0.5 * (start + end)
        return [(start, mid), (mid, end)]
    idx = inner_lo + int(np.argmin(smooth[inner_lo : inner_hi + 1]))
    local_peak = float(np.max(smooth)) + 1e-12
    thr = max(local_peak * 0.18, _rms_threshold(track))
    left = idx
    while left > 0 and smooth[left] < thr:
        left -= 1
    right = idx
    while right < len(times) - 1 and smooth[right] < thr:
        right += 1
    c1 = float(times[left])
    c2 = float(times[right])
    c1 = max(c1, start + min_span)
    c2 = min(c2, end - min_span)
    if c2 <= c1 + 0.01:
        cut = min(max(float(times[idx]), start + min_span), end - min_span)
        return [(start, cut), (cut, end)]
    return [(start, c1), (c2, end)]


def _match_islands(track: FrameTrack, duration: float, n: int) -> list[tuple[float, float]]:
    """Keep n speech islands. Extra fragments merge; missing islands are split at valleys."""
    if n <= 0:
        return []
    islands = _islands_from_mask(track, duration, _speech_mask(track, close_ms=30.0))
    if not islands:
        return []
    islands = list(islands)
    while len(islands) > n:
        gap_i = min(range(len(islands) - 1), key=lambda i: islands[i + 1][0] - islands[i][1])
        islands[gap_i] = (islands[gap_i][0], islands[gap_i + 1][1])
        del islands[gap_i + 1]
    while len(islands) < n:
        i = int(np.argmax([end - start for start, end in islands]))
        parts = _split_island_with_gap(track, islands[i][0], islands[i][1])
        if len(parts) != 2 or parts[1][0] <= parts[0][0] + 1e-6:
            start, end = islands[i]
            mid = 0.5 * (start + end)
            parts = [(start, mid), (mid, end)]
        islands = islands[:i] + parts + islands[i + 1 :]
    return islands


def chunk_time_bounds(tokens: list[Token], track: FrameTrack, duration: float) -> list[tuple[float, float]]:
    """Time range for each filename speech chunk, with genuine rests left as gaps."""
    chunks = speech_chunks(tokens)
    n = len(chunks)
    if n == 0:
        return []
    islands = _match_islands(track, duration, n)
    if len(islands) != n:
        start, end = _bounds_from_mask(track, duration)
        islands = _equal_bounds(start, end, n)
    for i, moras in enumerate(chunks):
        if moras and moras[0].is_stop:
            lo = 0.0 if i == 0 else islands[i - 1][1]
            islands[i] = (stop_closure_start(track, islands[i][0], lo), islands[i][1])
    for i in range(1, len(islands)):
        prev_end = islands[i - 1][1]
        start, end = islands[i]
        if start < prev_end:
            start = prev_end
        if end < start + 0.02:
            end = min(duration, start + 0.02)
        islands[i] = (start, end)
    return islands


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


def flap_onset(track: FrameTrack, peak: float, t_lo: float, t_hi: float) -> float:
    """Start of a flap: the drop into the tap, or the rise out of silence when initial."""
    t_lo = max(0.0, t_lo)
    peak = min(max(peak, t_lo), max(t_lo, t_hi))
    lo = max(t_lo, peak - 0.06)
    hi = min(t_hi, max(peak + 0.02, lo + 0.03))
    sel = (track.times >= lo) & (track.times <= hi)
    if int(np.count_nonzero(sel)) < 3:
        return max(t_lo, peak - 0.02)
    times = track.times[sel]
    rms = track.rms[sel]
    if len(times) < 3:
        return max(t_lo, peak - 0.02)
    speech_thr = _speech_thr(track)
    if float(rms[0]) < speech_thr * 1.5:
        i = 0
        while i < len(rms) - 1 and float(rms[i]) < speech_thr * 0.45:
            i += 1
        return max(t_lo, float(times[i]))
    i = int(np.argmin(np.diff(rms)))
    return max(t_lo, float(times[i]))


def flap_vowel_start(track: FrameTrack, start: float, end: float) -> float:
    """Vowel onset just after the short flap release."""
    span = end - start
    if span < 0.04:
        return start + span * 0.35
    hi = min(end - 0.015, start + 0.09)
    sel = (track.times >= start) & (track.times <= hi)
    if int(np.count_nonzero(sel)) < 3:
        cut = start + min(0.03, span * 0.3)
        return min(max(cut, start + 0.018), end - 0.015)
    times = track.times[sel]
    rms = track.rms[sel]
    search_end = max(1, len(times) - 1)
    dip_i = int(np.argmin(rms[:search_end]))
    tail = rms[dip_i:]
    if len(tail) >= 2:
        cut = float(times[dip_i + int(np.argmax(np.diff(tail))) + 1])
    else:
        cut = float(times[min(dip_i + 1, len(times) - 1)])
    cut = min(max(cut, start + 0.018), start + 0.06)
    return min(cut, end - 0.015)


def _flap_onsets(track: FrameTrack, start: float, end: float, count: int) -> list[float] | None:
    sel = (track.times >= start) & (track.times <= end)
    if not np.any(sel):
        return None
    times = track.times[sel]
    rms = track.rms[sel]
    score = np.zeros(len(rms))
    if len(rms) >= 3:
        score[1:-1] = 0.5 * (rms[:-2] + rms[2:]) - rms[1:-1]
    peaks = _spaced_peaks(times, score, count, min_dt=0.10)
    if peaks is None:
        return None
    onsets = [flap_onset(track, peak, start if i == 0 else peaks[i - 1], end) for i, peak in enumerate(peaks)]
    onsets[0] = start
    return onsets


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


def _median_smooth(values: np.ndarray, width: int = 3) -> np.ndarray:
    src = np.asarray(values, dtype=float)
    out = src.copy()
    half = width // 2
    n = len(src)
    for i in range(n):
        chunk = src[max(0, i - half) : min(n, i + half + 1)]
        finite = chunk[np.isfinite(chunk)]
        if finite.size:
            out[i] = float(np.median(finite))
    return out


def _abs_log_delta(values: np.ndarray) -> np.ndarray:
    src = np.asarray(values, dtype=float)
    delta = np.zeros(max(0, len(src) - 1), dtype=float)
    for i in range(len(delta)):
        a = src[i]
        b = src[i + 1]
        if np.isfinite(a) and np.isfinite(b) and a > 50.0 and b > 50.0:
            delta[i] = abs(float(np.log(b) - np.log(a)))
    return delta


def _strong_peak_index(score: np.ndarray, min_peak: float) -> int | None:
    if score.size == 0:
        return None
    peak = float(np.max(score))
    if peak < min_peak:
        return None
    baseline = float(np.percentile(score, 40))
    if peak < baseline * 1.8:
        return None
    return int(np.argmax(score))


def _formant_velocity_time(
    times: np.ndarray,
    f1: np.ndarray,
    f2: np.ndarray,
    t0: float,
    t1: float,
) -> float | None:
    sel = (times >= t0) & (times <= t1)
    if int(np.count_nonzero(sel)) < 4:
        return None
    t = times[sel]
    d1 = _abs_log_delta(_median_smooth(f1[sel]))
    d2 = _abs_log_delta(_median_smooth(f2[sel]))
    score = d1 + d2
    idx = _strong_peak_index(score, 0.06)
    if idx is None:
        return None
    return float(t[idx + 1])


def _strong_delta_time(times: np.ndarray, values: np.ndarray, t0: float, t1: float) -> float | None:
    sel = (times >= t0) & (times <= t1) & np.isfinite(values)
    if int(np.count_nonzero(sel)) < 4:
        return None
    t = times[sel]
    vals = values[sel]
    delta = np.abs(np.diff(vals))
    level = float(np.median(np.abs(vals)))
    idx = _strong_peak_index(delta, max(1e-6, 0.15 * level))
    if idx is None:
        return None
    return float(t[idx + 1])


def _rms_dip_near(track: FrameTrack, prior: float, lo: float, hi: float) -> float:
    if hi - lo < 0.03:
        return prior
    sel = (track.times >= lo) & (track.times <= hi)
    if int(np.count_nonzero(sel)) < 4:
        return prior
    times = track.times[sel]
    rms = track.rms[sel]
    smooth = np.convolve(rms, np.ones(3) / 3, mode="same")
    idx = int(np.argmin(smooth))
    if idx <= 0 or idx >= len(smooth) - 1:
        return prior
    if abs(float(times[idx]) - prior) > 0.08:
        return prior
    valley = float(smooth[idx])
    shoulder = float(max(smooth[0], smooth[-1]))
    if shoulder <= 1e-8 or valley > 0.72 * shoulder:
        return prior
    return float(times[idx])


def _open_span(start: float, end: float) -> tuple[float, float]:
    if end < start + 0.01:
        end = start + 0.02
    return start, end


def refine_vowel_hiatus(
    track: FrameTrack,
    prior: float,
    center_a: float,
    center_b: float,
    same_vowel: bool,
    formants: tuple[np.ndarray, np.ndarray, np.ndarray] | None = None,
) -> float:
    """Nudge an MMS vowel boundary between the two vowel centers.

    ``prior`` is the start of the second vowel. A weak spectral change keeps
    that time. Same vowels move only toward a clear RMS dip near it.
    """
    lo, hi = (center_a, center_b) if center_a <= center_b else (center_b, center_a)
    if hi <= lo:
        return prior
    prior = min(max(prior, lo), hi)
    if hi - lo < 0.02:
        return prior
    if same_vowel:
        return _rms_dip_near(track, prior, lo, hi)
    if formants is not None:
        moved = _formant_velocity_time(formants[0], formants[1], formants[2], lo, hi)
        return prior if moved is None else moved
    moved = _strong_delta_time(track.times, track.bright, lo, hi)
    return prior if moved is None else moved


def vowel_hiatus_onsets(
    track: FrameTrack,
    start: float,
    end: float,
    vowels: list[str | None],
    span_starts: list[float],
    span_ends: list[float],
    formants: tuple[np.ndarray, np.ndarray, np.ndarray] | None = None,
) -> list[float]:
    n = len(vowels)
    if n <= 1:
        return [start]
    onsets = [start]
    for i in range(1, n):
        a0, a1 = _open_span(span_starts[i - 1], span_ends[i - 1])
        b0, b1 = _open_span(span_starts[i], span_ends[i])
        same = vowels[i - 1] == vowels[i]
        onset = refine_vowel_hiatus(
            track,
            b0,
            0.5 * (a0 + a1),
            0.5 * (b0 + b1),
            same,
            formants,
        )
        onset = max(onset, onsets[-1] + 0.04)
        latest = end - 0.04 * (n - i)
        onset = min(onset, latest)
        if onset <= onsets[-1]:
            onset = min(end, onsets[-1] + 0.04)
        onsets.append(onset)
    onsets[0] = start
    return onsets


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


def vowel_boundary(
    track: FrameTrack,
    t_a: float,
    t_b: float,
    v_a: str | None,
    v_b: str | None,
) -> float:
    t0, t1 = (t_a, t_b) if t_a <= t_b else (t_b, t_a)
    mid = 0.5 * (t0 + t1)
    if t1 - t0 < 0.05:
        return mid
    if v_a == v_b:
        return _rms_valley(track, t0, t1, mid)
    return _max_delta_time(track.times, track.bright, t0, t1, mid)


def vowel_mora_onsets(
    track: FrameTrack,
    start: float,
    end: float,
    vowels: list[str | None],
) -> list[float]:
    n = len(vowels)
    if n <= 1:
        return [start]
    step = (end - start) / n
    onsets = [start]
    for i in range(1, n):
        guess = start + i * step
        lo = max(start, guess - step * 0.45)
        hi = min(end, guess + step * 0.45)
        onsets.append(vowel_boundary(track, lo, hi, vowels[i - 1], vowels[i]))
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
    if all(m.is_flap and m.needs_cv_split for m in moras):
        onsets = _flap_onsets(track, start, end, n)
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
    elif mora.is_flap:
        cut = flap_vowel_start(track, start, end)
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
        return [Span(start, end, phone_name(mora, "C"), mora.text)]
    if mora.kind == "V" or not mora.needs_cv_split:
        return [Span(start, end, phone_name(mora, "V"), mora.text)]
    cut = _cv_cut(track, mora, start, end)
    return [
        Span(start, cut, phone_name(mora, "C"), mora.text),
        Span(cut, end, phone_name(mora, "V"), mora.text),
    ]


def segment_tokens(tokens: list[Token], track: FrameTrack, duration: float) -> list[Span]:
    spans: list[Span] = []
    chunks = speech_chunks(tokens)
    leading_rest, trailing_rest, internal_rests = rest_layout(tokens)
    chunk_bounds = chunk_time_bounds(tokens, track, duration)
    speech_start = chunk_bounds[0][0] if chunk_bounds else 0.0

    if leading_rest:
        spans.append(Span(0.0, speech_start, SILENCE_LABEL, tokens[0].text))
    elif speech_start > 0.02:
        spans.append(Span(0.0, speech_start, SILENCE_LABEL, ""))

    if not chunks:
        if not leading_rest:
            spans.append(Span(0.0, duration, SILENCE_LABEL, "_"))
        elif speech_start < duration:
            spans.append(Span(speech_start, duration, SILENCE_LABEL, tokens[-1].text if trailing_rest else ""))
        return _clean(spans, duration)

    for i, moras in enumerate(chunks):
        c0, c1 = chunk_bounds[i]
        mora_bounds = _mora_bounds(track, moras, c0, c1)
        for mora, (m0, m1) in zip(moras, mora_bounds, strict=True):
            spans.extend(_label_mora(mora, m0, m1, track))
        if i < len(chunks) - 1:
            gap_end = chunk_bounds[i + 1][0]
            rest_text = internal_rests[i].text if i < len(internal_rests) else "_"
            if gap_end > c1 + 0.005:
                spans.append(Span(c1, gap_end, SILENCE_LABEL, rest_text))

    last_end = spans[-1].end if spans else speech_start
    if last_end < duration - 0.01:
        rest_text = tokens[-1].text if trailing_rest else ""
        spans.append(Span(last_end, duration, SILENCE_LABEL, rest_text))
    return _clean(spans, duration)


def _clean(spans: list[Span], duration: float) -> list[Span]:
    if not spans:
        return [Span(0.0, duration, SILENCE_LABEL, "")]
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
                out.append(Span(gap_start, start, SILENCE_LABEL, ""))
        out.append(Span(start, end, span.label, span.token))
    if out[0].start > 0.005:
        out.insert(0, Span(0.0, out[0].start, SILENCE_LABEL, ""))
    else:
        out[0] = Span(0.0, out[0].end, out[0].label, out[0].token)
    if duration - out[-1].end > 0.005:
        out.append(Span(out[-1].end, duration, SILENCE_LABEL, ""))
    else:
        last = out[-1]
        out[-1] = Span(last.start, duration, last.label, last.token)
    return out
