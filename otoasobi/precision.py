from __future__ import annotations

import numpy as np

from otoasobi.align_mms import AlignedChar, align_chars, available as mms_available
from otoasobi.features import FrameTrack
from otoasobi.parser import STOP, Token
from otoasobi.phones import mms_plan
from otoasobi.segment import Span, _bounds_from_mask, _clean, _cv_cut


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


def _refine_onset(track: FrameTrack, mora: Token, peak: float, t_lo: float, t_hi: float) -> float:
    t_lo = max(0.0, t_lo)
    peak = min(max(peak, t_lo + 0.01), max(t_lo + 0.02, t_hi))
    sel = (track.times >= t_lo) & (track.times <= peak + 0.02)
    if int(np.count_nonzero(sel)) < 3:
        return max(t_lo, peak - 0.05)
    times = track.times[sel]
    rms = track.rms[sel]
    ratio = track.ratio[sel]
    peak_i = int(np.argmin(np.abs(times - peak)))
    speech_thr = max(float(np.max(track.rms)) * 0.08, 1e-4)

    if mora.is_fricative:
        mask = (ratio > 1.25) & (rms > speech_thr * 0.2)
    elif mora.kind == "Q" or mora.consonant in STOP:
        vowel_thr = max(float(np.max(rms)) * 0.4, speech_thr)
        vowel_like = (rms > vowel_thr) & (ratio < 1.15)
        mask = ~vowel_like
    elif mora.kind == "V":
        mask = rms > speech_thr
    else:
        mask = rms > speech_thr * 0.35

    i = peak_i
    while i > 0 and bool(mask[i - 1]):
        i -= 1
    return float(times[i])


def _refine_cv(
    mora: Token,
    start: float,
    end: float,
    vowel_peak: float | None,
    track: FrameTrack,
    harm: tuple[np.ndarray, np.ndarray] | None,
) -> float:
    acoustic = _cv_cut(track, mora, start, end)
    if mora.is_fricative or vowel_peak is None:
        return acoustic
    guess = min(max(vowel_peak, start + 0.015), end - 0.015)
    if harm is not None:
        from otoasobi.praat_refine import voicing_onset

        h_times, h_vals = harm
        voiced = voicing_onset(h_times, h_vals, start, min(end, guess + 0.06), guess)
        if start + 0.018 < voiced < end - 0.018:
            if mora.consonant in STOP:
                return voiced
            if abs(voiced - acoustic) < 0.06:
                return voiced
    if mora.consonant in STOP:
        return min(guess, acoustic) if acoustic > start + 0.02 else guess
    return acoustic


def _mora_bounds_from_align(
    tokens: list[Token],
    aligned_by_index: dict[int, list[AlignedChar]],
    track: FrameTrack,
    speech_start: float,
    speech_end: float,
) -> dict[int, tuple[float, float]]:
    speech_indices = [i for i, token in enumerate(tokens) if not token.is_rest]
    if not speech_indices:
        return {}

    peaks: dict[int, float] = {}
    prev_vowel = speech_start
    raw_onsets: dict[int, float] = {}
    for i in speech_indices:
        chars = aligned_by_index[i]
        c_times = [c.time for c in chars if c.role == "C"]
        v_times = [c.time for c in chars if c.role == "V"]
        peak = c_times[0] if c_times else (v_times[0] if v_times else prev_vowel)
        peaks[i] = peak
        raw_onsets[i] = _refine_onset(track, tokens[i], peak, prev_vowel, speech_end)
        prev_vowel = v_times[0] if v_times else peak

    starts: list[float] = []
    for k, i in enumerate(speech_indices):
        if k == 0:
            starts.append(speech_start)
        else:
            onset = max(raw_onsets[i], starts[-1] + 0.04)
            onset = min(onset, peaks[i])
            starts.append(onset)

    bounds: dict[int, tuple[float, float]] = {}
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
    vowel_peak: float | None,
    harm: tuple[np.ndarray, np.ndarray] | None,
) -> list[Span]:
    if mora.kind in {"N", "Q"}:
        return [Span(start, end, "子音", mora.text)]
    if mora.kind == "V" or not mora.needs_cv_split:
        return [Span(start, end, "母音", mora.text)]
    cut = _refine_cv(mora, start, end, vowel_peak, track, harm)
    return [
        Span(start, cut, "子音", mora.text),
        Span(cut, end, "母音", mora.text),
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


def segment_precision(
    tokens: list[Token],
    y: np.ndarray,
    sr: int,
    track: FrameTrack,
    duration: float,
) -> tuple[list[Span], str]:
    if not mms_available():
        raise RuntimeError("MMS is not installed")

    rms_start, rms_end = _bounds_from_mask(track, duration)
    speech_start, speech_end = rms_start, rms_end
    harm = None
    bits: list[str] = []

    try:
        from otoasobi import praat_refine

        if praat_refine.available():
            p0, p1 = praat_refine.speech_bounds(y, sr, duration)
            if abs(p0 - rms_start) <= 0.12:
                speech_start = p0
            if abs(p1 - rms_end) <= 0.12:
                speech_end = p1
            harm = praat_refine.harmonicity(y, sr)
            bits.append("praat")
    except Exception:
        harm = None

    plan = mms_plan(tokens)
    if not plan:
        raise RuntimeError("no speech phones to align")
    aligned = align_chars(y, sr, plan)
    bits.append("mms")
    grouped = _group(aligned)

    speech_indices = [i for i, token in enumerate(tokens) if not token.is_rest]
    missing = [i for i in speech_indices if i not in grouped]
    if missing:
        raise RuntimeError(f"MMS missed tokens {missing}")

    mora_bounds = _mora_bounds_from_align(tokens, grouped, track, speech_start, speech_end)
    vowel_peaks = {
        i: next((c.time for c in grouped[i] if c.role == "V"), None) for i in speech_indices
    }

    leading_rest = bool(tokens) and tokens[0].is_rest
    trailing_rest = bool(tokens) and tokens[-1].is_rest
    rest_tokens = [t for t in tokens if t.is_rest]
    internal_rests = rest_tokens[1:] if leading_rest else rest_tokens
    if trailing_rest and internal_rests:
        internal_rests = internal_rests[:-1]

    spans: list[Span] = []
    if leading_rest:
        spans.append(Span(0.0, speech_start, "無音", tokens[0].text))
    elif speech_start > 0.02:
        spans.append(Span(0.0, speech_start, "無音", ""))

    index_chunks = _speech_token_indices(tokens)
    if not index_chunks:
        if speech_end < duration:
            spans.append(Span(speech_start, duration, "無音", tokens[-1].text if trailing_rest else ""))
        engine = "mms+praat" if "praat" in bits else "mms"
        return _clean(spans, duration), engine

    for i, indices in enumerate(index_chunks):
        for idx in indices:
            start, end = mora_bounds[idx]
            spans.extend(
                _label_with_cut(tokens[idx], start, end, track, vowel_peaks[idx], harm)
            )
        if i < len(index_chunks) - 1:
            last_end = mora_bounds[indices[-1]][1]
            next_start = mora_bounds[index_chunks[i + 1][0]][0]
            if next_start > last_end + 0.005:
                rest_text = internal_rests[i].text if i < len(internal_rests) else "_"
                spans.append(Span(last_end, next_start, "無音", rest_text))

    last_end = spans[-1].end if spans else speech_end
    if last_end < duration - 0.01:
        rest_text = tokens[-1].text if trailing_rest else ""
        spans.append(Span(last_end, duration, "無音", rest_text))

    engine = "mms+praat" if "praat" in bits else "mms"
    return _clean(spans, duration), engine
