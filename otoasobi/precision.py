from __future__ import annotations

import numpy as np

from otoasobi.align_mms import AlignedChar, align_chars, available as mms_available
from otoasobi.features import FrameTrack
from otoasobi.parser import Token, rest_layout
from otoasobi.phones import SILENCE_LABEL, MmsUnit, mms_plan, phone_name, token_mms_units
from otoasobi.segment import (
    Span,
    _clean,
    _cv_cut,
    chunk_time_bounds,
    nasal_onset,
    nasal_vowel_start,
    stop_closure_start,
    stop_vowel_start,
    vowel_boundary,
    vowel_mora_onsets,
)


def warmup() -> bool:
    if not mms_available():
        return False
    from otoasobi.align_mms import load_model

    load_model()
    return True


def _group(aligned: list[AlignedChar]) -> dict[int, list[AlignedChar]]:
    grouped: dict[int, list[AlignedChar]] = {}
    for item in aligned:
        grouped.setdefault(item.token_index, []).append(item)
    return grouped


def _mora_onset(
    track: FrameTrack,
    mora: Token,
    peak: float,
    t_lo: float,
    t_hi: float,
) -> float:
    if mora.is_fricative:
        sel = (track.times >= t_lo) & (track.times <= peak + 0.02)
        if int(np.count_nonzero(sel)) < 3:
            return max(t_lo, peak - 0.05)
        times = track.times[sel]
        rms = track.rms[sel]
        ratio = track.ratio[sel]
        peak_i = int(np.argmin(np.abs(times - peak)))
        speech_thr = max(float(np.max(track.rms)) * 0.08, 1e-4)
        mask = (ratio > 1.25) & (rms > speech_thr * 0.2)
        i = peak_i
        while i > 0 and bool(mask[i - 1]):
            i -= 1
        return float(times[i])
    if mora.is_stop:
        return stop_closure_start(track, peak, t_lo)
    if mora.is_nasal:
        return nasal_onset(track, peak, t_lo, t_hi)
    sel = (track.times >= t_lo) & (track.times <= peak + 0.02)
    if int(np.count_nonzero(sel)) < 3:
        return max(t_lo, peak - 0.04)
    times = track.times[sel]
    rms = track.rms[sel]
    peak_i = int(np.argmin(np.abs(times - peak)))
    speech_thr = max(float(np.max(track.rms)) * 0.08, 1e-4)
    i = peak_i
    while i > 0 and rms[i - 1] > speech_thr * 0.35:
        i -= 1
    return float(times[i])


def _refine_cv(
    mora: Token,
    start: float,
    end: float,
    c_peak: float | None,
    vowel_peak: float | None,
    track: FrameTrack,
    harm: tuple[np.ndarray, np.ndarray] | None,
) -> float:
    if mora.is_fricative:
        return _cv_cut(track, mora, start, end)
    if mora.is_stop:
        burst = c_peak if c_peak is not None else start
        cut = stop_vowel_start(track, start, end)
        cut = max(cut, burst + 0.015)
        if vowel_peak is not None and vowel_peak > burst:
            cut = min(cut, 0.55 * burst + 0.45 * vowel_peak)
        if harm is not None:
            from otoasobi.praat_refine import voicing_onset

            voiced = voicing_onset(harm[0], harm[1], burst, min(end, (vowel_peak or end)), cut)
            if burst + 0.01 <= voiced <= min(cut + 0.04, end - 0.015):
                cut = voiced
        return min(max(cut, start + 0.02), end - 0.02)
    if mora.is_nasal:
        if c_peak is not None and vowel_peak is not None and vowel_peak > c_peak + 0.02:
            cut = 0.5 * (c_peak + vowel_peak)
        else:
            cut = nasal_vowel_start(track, start, end)
        return min(max(cut, start + 0.02), end - 0.02)
    if vowel_peak is not None:
        return min(max(vowel_peak, start + 0.015), end - 0.015)
    return _cv_cut(track, mora, start, end)


def _mora_bounds_from_align(
    tokens: list[Token],
    aligned_by_index: dict[int, list[AlignedChar]],
    track: FrameTrack,
    speech_start: float,
    speech_end: float,
    leading_rest: bool,
    f2: tuple[np.ndarray, np.ndarray] | None = None,
    speech_indices: list[int] | None = None,
) -> dict[int, tuple[float, float]]:
    if speech_indices is None:
        speech_indices = [i for i, token in enumerate(tokens) if not token.is_rest]
    if not speech_indices:
        return {}

    peaks: dict[int, float] = {}
    vowel_by_index: dict[int, float | None] = {}
    for i in speech_indices:
        chars = aligned_by_index[i]
        c_times = [c.time for c in chars if c.role == "C"]
        v_times = [c.time for c in chars if c.role == "V"]
        peak = c_times[0] if c_times else (v_times[0] if v_times else speech_start)
        peaks[i] = peak
        vowel_by_index[i] = v_times[0] if v_times else None

    moras = [tokens[i] for i in speech_indices]
    if all(m.is_vowel_only for m in moras):
        onsets = vowel_mora_onsets(
            track,
            speech_start,
            speech_end,
            [m.vowel for m in moras],
            [peaks[i] for i in speech_indices],
            f2,
        )
        bounds: dict[int, tuple[float, float]] = {}
        for k, i in enumerate(speech_indices):
            start = onsets[k]
            end = onsets[k + 1] if k + 1 < len(onsets) else speech_end
            if end <= start + 0.04:
                end = min(speech_end, start + 0.08)
            bounds[i] = (start, end)
        return bounds

    prev_limit = speech_start
    raw_onsets: dict[int, float] = {}
    for i in speech_indices:
        mora = tokens[i]
        peak = peaks[i]
        if mora.is_vowel_only:
            raw_onsets[i] = peak
        else:
            raw_onsets[i] = _mora_onset(track, mora, peak, prev_limit, speech_end)
        v_peak = vowel_by_index[i]
        if v_peak is not None:
            prev_limit = v_peak + 0.05
        else:
            prev_limit = peak + 0.04

    first = speech_indices[0]
    first_mora = tokens[first]
    if first_mora.is_stop:
        speech_start = raw_onsets[first]
        if leading_rest:
            speech_start = max(speech_start, min(0.04, peaks[first] * 0.3))
    elif first_mora.is_vowel_only:
        speech_start = min(raw_onsets[first], speech_start)
        speech_start = min(speech_start, peaks[first])
    else:
        speech_start = min(raw_onsets[first], speech_start)

    starts: list[float] = []
    for k, i in enumerate(speech_indices):
        if k == 0:
            starts.append(speech_start)
            continue
        prev_i = speech_indices[k - 1]
        prev_mora = tokens[prev_i]
        mora = tokens[i]
        floor = starts[-1] + 0.06
        if prev_mora.is_vowel_only and mora.is_vowel_only:
            prev_peak = vowel_by_index[prev_i] or peaks[prev_i]
            curr_peak = vowel_by_index[i] or peaks[i]
            mid = 0.5 * (prev_peak + curr_peak)
            onset = vowel_boundary(track, prev_peak, curr_peak, prev_mora.vowel, mora.vowel, f2)
            if abs(onset - mid) > 0.04:
                onset = mid
            onset = max(onset, floor)
            starts.append(onset)
            continue
        prev_v = vowel_by_index[prev_i]
        if prev_v is not None:
            floor = max(floor, prev_v + 0.045)
        onset = max(raw_onsets[i], floor)
        starts.append(onset)

    bounds = {}
    for k, i in enumerate(speech_indices):
        start = starts[k]
        end = starts[k + 1] if k + 1 < len(starts) else speech_end
        if end <= start + 0.03:
            end = min(speech_end, start + 0.08)
        bounds[i] = (start, end)
    return bounds


def _label_with_cut(
    mora: Token,
    start: float,
    end: float,
    track: FrameTrack,
    c_peak: float | None,
    vowel_peak: float | None,
    harm: tuple[np.ndarray, np.ndarray] | None,
) -> list[Span]:
    if mora.kind in {"N", "Q"}:
        return [Span(start, end, phone_name(mora, "C"), mora.text)]
    if mora.kind == "V" or not mora.needs_cv_split:
        return [Span(start, end, phone_name(mora, "V"), mora.text)]
    cut = _refine_cv(mora, start, end, c_peak, vowel_peak, track, harm)
    return [
        Span(start, cut, phone_name(mora, "C"), mora.text),
        Span(cut, end, phone_name(mora, "V"), mora.text),
    ]


def _speech_token_indices(tokens: list[Token]) -> list[list[int]]:
    chunks: list[list[int]] = []
    current: list[int] = []
    for i, token in enumerate(tokens):
        if token.is_rest:
            if current:
                chunks.append(current)
                current = []
        else:
            current.append(i)
    if current:
        chunks.append(current)
    return chunks


def _plan_for_indices(tokens: list[Token], indices: list[int]) -> list[MmsUnit]:
    plan: list[MmsUnit] = []
    for i in indices:
        plan.extend(token_mms_units(tokens[i], i))
    return plan


def _align_region(
    y: np.ndarray,
    sr: int,
    units: list[MmsUnit],
    t0: float,
    t1: float,
) -> list[AlignedChar]:
    i0 = max(0, int(round(t0 * sr)))
    i1 = min(len(y), int(round(t1 * sr)))
    if i1 - i0 < int(0.08 * sr):
        raise RuntimeError("region too short for MMS")
    aligned = align_chars(y[i0:i1], sr, units)
    offset = i0 / float(sr)
    return [
        AlignedChar(
            char=item.char,
            time=item.time + offset,
            score=item.score,
            token_index=item.token_index,
            role=item.role,
        )
        for item in aligned
    ]


def _align_chunks(
    y: np.ndarray,
    sr: int,
    tokens: list[Token],
    index_chunks: list[list[int]],
    chunk_bounds: list[tuple[float, float]],
    duration: float,
) -> list[AlignedChar]:
    aligned: list[AlignedChar] = []
    pad = 0.04
    for i, indices in enumerate(index_chunks):
        plan = _plan_for_indices(tokens, indices)
        if not plan:
            continue
        c0, c1 = chunk_bounds[i]
        t0 = max(0.0, c0 - pad)
        t1 = min(duration, c1 + pad)
        if i > 0:
            t0 = max(t0, chunk_bounds[i - 1][1])
        if i + 1 < len(chunk_bounds):
            t1 = min(t1, chunk_bounds[i + 1][0])
        if t1 <= t0 + 0.05:
            t0, t1 = c0, max(c1, c0 + 0.08)
        aligned.extend(_align_region(y, sr, plan, t0, t1))
    return aligned


def segment_precision(
    tokens: list[Token],
    y: np.ndarray,
    sr: int,
    track: FrameTrack,
    duration: float,
) -> tuple[list[Span], str]:
    if not mms_available():
        raise RuntimeError("MMS is not installed")

    plan = mms_plan(tokens)
    if not plan:
        raise RuntimeError("no speech phones to align")

    index_chunks = _speech_token_indices(tokens)
    chunk_bounds = list(chunk_time_bounds(tokens, track, duration))
    first_speech = next((t for t in tokens if not t.is_rest), None)
    harm = None
    f2 = None
    bits: list[str] = []

    try:
        from otoasobi import praat_refine

        if praat_refine.available() and chunk_bounds:
            p0, p1 = praat_refine.speech_bounds(y, sr, duration)
            c0, _c1 = chunk_bounds[0]
            if abs(p0 - c0) <= 0.15:
                start = p0
                if first_speech is not None and first_speech.is_stop:
                    start = stop_closure_start(track, start, 0.0)
                chunk_bounds[0] = (start, chunk_bounds[0][1])
            _cl0, cl1 = chunk_bounds[-1]
            if abs(p1 - cl1) <= 0.12:
                chunk_bounds[-1] = (
                    chunk_bounds[-1][0],
                    min(duration, max(chunk_bounds[-1][0] + 0.03, p1)),
                )
            harm = praat_refine.harmonicity(y, sr)
            f2 = praat_refine.formant_f2(y, sr)
            bits.append("praat")
    except Exception:
        harm = None
        f2 = None

    try:
        aligned = _align_chunks(y, sr, tokens, index_chunks, chunk_bounds, duration)
        bits.append("mms")
    except Exception:
        aligned = align_chars(y, sr, plan)
        bits.append("mms")

    grouped = _group(aligned)
    speech_indices = [i for chunk in index_chunks for i in chunk]
    missing = [i for i in speech_indices if i not in grouped]
    if missing:
        aligned = align_chars(y, sr, plan)
        grouped = _group(aligned)
        missing = [i for i in speech_indices if i not in grouped]
        if missing:
            raise RuntimeError(f"MMS missed tokens {missing}")

    leading_rest, trailing_rest, internal_rests = rest_layout(tokens)
    mora_bounds: dict[int, tuple[float, float]] = {}
    for i, indices in enumerate(index_chunks):
        c0, c1 = chunk_bounds[i]
        mora_bounds.update(
            _mora_bounds_from_align(
                tokens,
                grouped,
                track,
                c0,
                c1,
                leading_rest and i == 0,
                f2,
                indices,
            )
        )
        for idx in indices:
            start, end = mora_bounds[idx]
            start = min(max(start, c0), c1 - 0.02)
            end = max(min(end, c1), start + 0.02)
            mora_bounds[idx] = (start, end)

    speech_start = mora_bounds[speech_indices[0]][0] if speech_indices else 0.0
    speech_end = mora_bounds[speech_indices[-1]][1] if speech_indices else duration
    vowel_peaks = {
        i: next((c.time for c in grouped[i] if c.role == "V"), None) for i in speech_indices
    }
    c_peaks = {
        i: next((c.time for c in grouped[i] if c.role == "C"), None) for i in speech_indices
    }

    spans: list[Span] = []
    if leading_rest:
        spans.append(Span(0.0, speech_start, SILENCE_LABEL, tokens[0].text))
    elif speech_start > 0.02:
        spans.append(Span(0.0, speech_start, SILENCE_LABEL, ""))

    if not index_chunks:
        if speech_end < duration:
            spans.append(Span(speech_start, duration, SILENCE_LABEL, tokens[-1].text if trailing_rest else ""))
        engine = "mms+praat" if "praat" in bits else "mms"
        return _clean(spans, duration), engine

    for i, indices in enumerate(index_chunks):
        for idx in indices:
            start, end = mora_bounds[idx]
            spans.extend(
                _label_with_cut(
                    tokens[idx],
                    start,
                    end,
                    track,
                    c_peaks[idx],
                    vowel_peaks[idx],
                    harm,
                )
            )
        if i < len(index_chunks) - 1:
            last_end = mora_bounds[indices[-1]][1]
            next_start = mora_bounds[index_chunks[i + 1][0]][0]
            if next_start > last_end + 0.005:
                rest_text = internal_rests[i].text if i < len(internal_rests) else "_"
                spans.append(Span(last_end, next_start, SILENCE_LABEL, rest_text))

    last_end = spans[-1].end if spans else speech_end
    if last_end < duration - 0.01:
        rest_text = tokens[-1].text if trailing_rest else ""
        spans.append(Span(last_end, duration, SILENCE_LABEL, rest_text))

    engine = "mms+praat" if "praat" in bits else "mms"
    return _clean(spans, duration), engine
