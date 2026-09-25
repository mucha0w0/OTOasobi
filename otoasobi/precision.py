from __future__ import annotations

import numpy as np

from otoasobi.align_mms import AlignedChar, align_chars, available as mms_available
from otoasobi.features import FrameTrack
from otoasobi.parser import Token, rest_layout, speech_group_chunks
from otoasobi.phones import SILENCE_LABEL, MmsUnit, phone_name, token_mms_units
from otoasobi.segment import (
    Span,
    _clean,
    _cv_cut,
    chunk_time_bounds,
    flap_onset,
    flap_vowel_start,
    labial_onset,
    labial_vowel_start,
    weak_fricative_onset,
    weak_fricative_vowel_start,
    label_grouped_mora,
    nasal_onset,
    nasal_vowel_start,
    stop_closure_start,
    stop_vowel_start,
    refine_vowel_hiatus,
    split_leading_breath,
    vowel_hiatus_onsets,
    _sonorant_join,
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
    if mora.is_labial_fricative:
        return labial_onset(track, peak, t_lo, t_hi)
    if mora.is_weak_fricative:
        return weak_fricative_onset(track, peak, t_lo, t_hi)
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
    if mora.is_flap:
        return flap_onset(track, peak, t_lo, t_hi)
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
    if mora.is_labial_fricative:
        cut = labial_vowel_start(track, start, end)
        if vowel_peak is not None and start + 0.03 <= vowel_peak <= start + 0.12:
            cut = 0.65 * cut + 0.35 * vowel_peak
        lo = start + 0.035
        hi = min(end - 0.03, start + 0.12)
        if hi <= lo:
            return start + (end - start) * 0.4
        return min(max(cut, lo), hi)
    if mora.is_weak_fricative:
        cut = weak_fricative_vowel_start(track, start, end)
        # A late MMS vowel can confirm the rise. An early one is the miss
        # this path exists to correct, so it must not pull the cut back.
        if vowel_peak is not None and cut - 0.03 <= vowel_peak <= cut + 0.04:
            cut = 0.75 * cut + 0.25 * vowel_peak
        lo = start + 0.03
        hi = min(end - 0.025, start + 0.20)
        if hi <= lo:
            return start + (end - start) * 0.4
        return min(max(cut, lo), hi)
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
    if mora.is_flap:
        cut = flap_vowel_start(track, start, end)
        if c_peak is not None:
            cut = max(cut, c_peak + 0.012)
        return min(max(cut, start + 0.018), min(end - 0.02, start + 0.06))
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
    formants: tuple[np.ndarray, np.ndarray, np.ndarray] | None = None,
    speech_indices: list[int] | None = None,
) -> dict[int, tuple[float, float]]:
    if speech_indices is None:
        speech_indices = [i for i, token in enumerate(tokens) if not token.is_rest]
    if not speech_indices:
        return {}

    peaks: dict[int, float] = {}
    vowel_by_index: dict[int, float | None] = {}
    vowel_spans: dict[int, tuple[float, float]] = {}
    nucleus_spans: dict[int, tuple[float, float]] = {}
    for i in speech_indices:
        chars = aligned_by_index[i]
        c_chars = [c for c in chars if c.role == "C"]
        v_chars = [c for c in chars if c.role == "V"]
        if v_chars:
            vowel_spans[i] = (v_chars[0].time, v_chars[0].end)
            vowel_by_index[i] = v_chars[0].time
            nucleus_spans[i] = vowel_spans[i]
        else:
            vowel_by_index[i] = None
            if tokens[i].kind == "N" and c_chars:
                nucleus_spans[i] = (c_chars[0].time, c_chars[-1].end)
        peak = c_chars[0].time if c_chars else (vowel_by_index[i] if vowel_by_index[i] is not None else speech_start)
        peaks[i] = peak

    moras = [tokens[i] for i in speech_indices]
    if all(m.is_vowel_only for m in moras):
        span_starts: list[float] = []
        span_ends: list[float] = []
        for i in speech_indices:
            if i in vowel_spans:
                span_starts.append(vowel_spans[i][0])
                span_ends.append(vowel_spans[i][1])
            else:
                span_starts.append(peaks[i])
                span_ends.append(peaks[i] + 0.02)
        onsets = vowel_hiatus_onsets(
            track,
            speech_start,
            speech_end,
            [m.vowel for m in moras],
            span_starts,
            span_ends,
            formants,
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
        # 「あお」「かお」「あん」「んあ」。子音が間に入る「んか」はここを通らない。
        if _sonorant_join(prev_mora, mora):
            span_a = nucleus_spans.get(prev_i)
            span_b = nucleus_spans.get(i)
            if span_a is not None and span_b is not None:
                a0, a1 = span_a
                b0, b1 = span_b
                if a1 < a0 + 0.01:
                    a1 = a0 + 0.02
                if b1 < b0 + 0.01:
                    b1 = b0 + 0.02
                onset = refine_vowel_hiatus(
                    track,
                    b0,
                    0.5 * (a0 + a1),
                    0.5 * (b0 + b1),
                    prev_mora.vowel is not None and prev_mora.vowel == mora.vowel,
                    formants,
                )
            else:
                onset = peaks[i]
            onset = max(onset, starts[-1] + 0.04)
            starts.append(onset)
            continue
        floor = starts[-1] + 0.06
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
            end=item.end + offset,
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

    group_chunks = speech_group_chunks(tokens)
    index_chunks = [[group.index for group in chunk] for chunk in group_chunks]
    plan = _plan_for_indices(tokens, [index for chunk in index_chunks for index in chunk])
    if not plan:
        raise RuntimeError("no speech phones to align")

    chunk_bounds = list(chunk_time_bounds(tokens, track, duration))
    first_speech = next((t for t in tokens if not t.is_rest), None)
    harm = None
    formants = None
    bits: list[str] = []

    try:
        from otoasobi import praat_refine

        if praat_refine.available() and chunk_bounds:
            p0, p1 = praat_refine.speech_bounds(y, sr, duration)
            c0, _c1 = chunk_bounds[0]
            if abs(p0 - c0) <= 0.15:
                start = p0
                # Intensity also rises on a loud inhale. The chunk start was
                # already moved past that hump; don't pull it back.
                split = split_leading_breath(track, 0.0, chunk_bounds[0][1])
                if split is not None and start < split[0] - 0.01:
                    start = c0
                elif first_speech is not None and first_speech.is_stop:
                    start = stop_closure_start(track, start, 0.0)
                chunk_bounds[0] = (start, chunk_bounds[0][1])
            _cl0, cl1 = chunk_bounds[-1]
            if abs(p1 - cl1) <= 0.12:
                chunk_bounds[-1] = (
                    chunk_bounds[-1][0],
                    min(duration, max(chunk_bounds[-1][0] + 0.03, p1)),
                )
            harm = praat_refine.harmonicity(y, sr)
            formants = praat_refine.formant_tracks(y, sr)
            bits.append("praat")
    except Exception:
        harm = None
        formants = None

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
                formants,
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

    for i, groups in enumerate(group_chunks):
        for group in groups:
            start, end = mora_bounds[group.index]

            def label_head(
                mora: Token,
                a: float,
                b: float,
                idx: int = group.index,
            ) -> list[Span]:
                return _label_with_cut(mora, a, b, track, c_peaks[idx], vowel_peaks[idx], harm)

            spans.extend(label_grouped_mora(group, start, end, label_head))
        if i < len(group_chunks) - 1:
            last_end = mora_bounds[groups[-1].index][1]
            next_start = mora_bounds[group_chunks[i + 1][0].index][0]
            if next_start > last_end + 0.005:
                rest_text = internal_rests[i].text if i < len(internal_rests) else "_"
                spans.append(Span(last_end, next_start, SILENCE_LABEL, rest_text))

    last_end = spans[-1].end if spans else speech_end
    if last_end < duration - 0.01:
        rest_text = tokens[-1].text if trailing_rest else ""
        spans.append(Span(last_end, duration, SILENCE_LABEL, rest_text))

    engine = "mms+praat" if "praat" in bits else "mms"
    return _clean(spans, duration), engine
