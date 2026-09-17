from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from otoasobi.features import FrameTrack, close_short_gaps, runs
from otoasobi.parser import STOP, Token, speech_chunks


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


def _equal_bounds(start: float, end: float, count: int) -> list[tuple[float, float]]:
    if count <= 0:
        return []
    step = (end - start) / count
    return [(start + i * step, start + (i + 1) * step) for i in range(count)]


def _mora_bounds(track: FrameTrack, moras: list[Token], start: float, end: float) -> list[tuple[float, float]]:
    n = len(moras)
    if n == 0:
        return []
    if n == 1:
        return [(start, end)]
    if all(m.is_fricative and m.needs_cv_split for m in moras):
        onsets = _fricative_onsets(track, start, end, n)
        if onsets is not None:
            bounds = []
            for i, onset in enumerate(onsets):
                nxt = onsets[i + 1] if i + 1 < n else end
                bounds.append((onset, nxt))
            bounds[0] = (start, bounds[0][1])
            bounds[-1] = (bounds[-1][0], end)
            return bounds
    return _equal_bounds(start, end, n)


def _cv_cut(track: FrameTrack, mora: Token, start: float, end: float) -> float:
    if end - start < 0.04:
        return start + (end - start) * 0.35
    sel = (track.times >= start) & (track.times <= end)
    if not np.any(sel):
        return start + (end - start) * 0.3
    times = track.times[sel]
    ratio = track.ratio[sel]
    zcr = track.zcr[sel]
    inner = slice(1, max(2, len(times) - 1))
    times_i = times[inner]
    if mora.is_fricative:
        score = ratio[inner]
        thr = 1.2
        vowelish = score < thr
        if np.any(vowelish):
            cut = float(times_i[int(np.argmax(vowelish))])
        else:
            cut = start + (end - start) * 0.28
    elif mora.consonant in STOP:
        flux = track.flux[sel][inner]
        cut = float(times_i[int(np.argmax(flux))]) if len(times_i) else start + 0.02
    else:
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
