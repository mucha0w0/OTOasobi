"""Label an audible inhale in the silence before the first phone.

The speech onset is taken from the finished alignment. Only the audio before
that time is examined, so fricatives and aspiration inside the phrase are
left alone. A breath is energy above the noise floor, little harmonicity,
and a noise-like spectrum, held long enough that a click does not qualify.
"""

from __future__ import annotations

import numpy as np

from otoasobi.features import FrameTrack, close_short_gaps, runs
from otoasobi.phones import BREATH_LABEL, SILENCE_LABEL, phone_kind
from otoasobi.segment import Span

MIN_DUR = 0.08
ONSET_GUARD = 0.02
ENERGY_MARGIN_DB = 8.0
HNR_MAX = 5.0
CENTROID_LO = 700.0
CENTROID_HI = 6500.0
GAP_S = 0.04
_SPEECH_KINDS = {"子音", "母音"}


def apply_leading_breath(
    spans: list[Span],
    y: np.ndarray,
    sr: int,
    track: FrameTrack,
) -> list[Span]:
    onset = _speech_onset(spans)
    if onset is None or onset < MIN_DUR + ONSET_GUARD:
        return spans
    regions = detect_leading_breaths(y, sr, track, onset)
    if not regions:
        return spans
    token = ""
    for span in spans:
        if span.end <= onset + 1e-4 and phone_kind(span.label) == SILENCE_LABEL:
            token = span.token
            break
    prefix = _cover(0.0, onset, regions, token)
    body: list[Span] = []
    for span in spans:
        if span.end <= onset + 1e-4:
            continue
        if span.start < onset:
            body.append(Span(onset, span.end, span.label, span.token))
        else:
            body.append(span)
    return prefix + body


def detect_leading_breaths(
    y: np.ndarray,
    sr: int,
    track: FrameTrack,
    onset: float,
) -> list[tuple[float, float]]:
    limit = onset - ONSET_GUARD
    if limit < MIN_DUR or sr <= 0 or len(y) == 0:
        return []
    times, energy_db, harmonicity = _cues(y, sr, track)
    if times.size == 0:
        return []
    finite_energy = energy_db[np.isfinite(energy_db)]
    if finite_energy.size < 3:
        return []
    # Floor comes from the whole file. The prefix is often mostly the breath,
    # so a prefix-only percentile would sit inside the breath and hide it.
    noise_db = float(np.percentile(finite_energy, 12))
    energetic = np.isfinite(energy_db) & (energy_db > noise_db + ENERGY_MARGIN_DB)
    # Praat intensity smears a click into a breath-length bump. RMS does not.
    rms_db = 20.0 * np.log10(np.maximum(track.rms, 1e-8))
    rms_noise = float(np.percentile(rms_db[np.isfinite(rms_db)], 12))
    rms_hot = _sample(track.times, (rms_db > rms_noise + ENERGY_MARGIN_DB).astype(float), times) > 0.5
    energetic = energetic & rms_hot
    if harmonicity is None:
        ratio = _sample(track.times, track.ratio, times)
        unvoiced = ratio >= 0.30
    else:
        unvoiced = ~np.isfinite(harmonicity) | (harmonicity <= HNR_MAX)
    centroid = _sample(track.times, track.centroid, times)
    noise_like = (centroid >= CENTROID_LO) & (centroid <= CENTROID_HI)
    mask = energetic & unvoiced & noise_like & (times < limit)
    hop = float(np.median(np.diff(times))) if len(times) >= 2 else 0.01
    max_gap = max(1, int(round(GAP_S / max(hop, 1e-3))))
    mask = close_short_gaps(mask, max_gap)
    found: list[tuple[float, float]] = []
    for start_i, end_i in runs(mask):
        start = max(0.0, float(times[start_i]) - hop / 2)
        end = min(limit, float(times[end_i - 1]) + hop / 2)
        if end - start >= MIN_DUR:
            found.append((start, end))
    return found


def _speech_onset(spans: list[Span]) -> float | None:
    for span in spans:
        if phone_kind(span.label) in _SPEECH_KINDS:
            return span.start
    return None


def _cover(
    start: float,
    end: float,
    regions: list[tuple[float, float]],
    silence_token: str,
) -> list[Span]:
    pieces: list[Span] = []
    cursor = start
    for raw_start, raw_end in regions:
        breath_start = max(raw_start, start)
        breath_end = min(raw_end, end)
        if breath_end - breath_start < 0.005:
            continue
        if breath_start > cursor + 0.005:
            pieces.append(Span(cursor, breath_start, SILENCE_LABEL, silence_token))
        pieces.append(Span(breath_start, breath_end, BREATH_LABEL, ""))
        cursor = breath_end
    if end > cursor + 0.005:
        pieces.append(Span(cursor, end, SILENCE_LABEL, silence_token))
    return pieces


def _cues(
    y: np.ndarray,
    sr: int,
    track: FrameTrack,
) -> tuple[np.ndarray, np.ndarray, np.ndarray | None]:
    praat = _praat_cues(y, sr)
    if praat is not None:
        return praat
    energy_db = 20.0 * np.log10(np.maximum(track.rms, 1e-8))
    return track.times, energy_db, None


def _praat_cues(
    y: np.ndarray,
    sr: int,
) -> tuple[np.ndarray, np.ndarray, np.ndarray] | None:
    try:
        from otoasobi import praat_refine

        if not praat_refine.available():
            return None
        level_t, level = praat_refine.intensity(y, sr)
        harm_t, harm = praat_refine.harmonicity(y, sr)
    except Exception:
        return None
    if level_t.size == 0:
        return None
    harmonicity = _sample(harm_t, harm, level_t, nearest=True)
    return level_t, level, harmonicity


def _sample(
    src_t: np.ndarray,
    src_v: np.ndarray,
    dst_t: np.ndarray,
    nearest: bool = False,
) -> np.ndarray:
    if src_t.size == 0 or dst_t.size == 0:
        return np.full(dst_t.shape, np.nan)
    if not nearest:
        return np.interp(dst_t, src_t, src_v, left=np.nan, right=np.nan)
    idx = np.searchsorted(src_t, dst_t, side="left")
    idx = np.clip(idx, 0, len(src_t) - 1)
    prev = np.clip(idx - 1, 0, len(src_t) - 1)
    use_prev = np.abs(src_t[prev] - dst_t) <= np.abs(src_t[idx] - dst_t)
    chosen = np.where(use_prev, prev, idx)
    out = np.asarray(src_v[chosen], dtype=float)
    out[np.abs(src_t[chosen] - dst_t) > 0.02] = np.nan
    return out
