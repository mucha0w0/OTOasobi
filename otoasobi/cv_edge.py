"""Move a coarse consonant-vowel cut to the left edge of the vowel.

The coarse cut only has to land in the right mora. This search then walks
backward from the vowel, on a 2 ms grid, and stops at the first stretch that
is no longer the vowel. Obstruents end where voicing and the low spectrum
begin, after the burst. Nasals end where F1 or the band shape leaves the
vowel nucleus. A palatal glide (じゃ, きゃ) counts as already past the
consonant: the cut is j → y, not the later change from y into the vowel.
Weak fricatives and flaps end where the level rises. A cue that never
fails leaves the coarse time alone.
"""

from __future__ import annotations

import numpy as np

from otoasobi.features import FrameTrack, close_short_gaps, local_edge_cues, runs
from otoasobi.parser import Token
from otoasobi.segment import sibilant_release

_STEP = 0.002
_SLACK = 0.04
# じゃ is j → y → a. The y glide is already past the consonant, and it can
# last longer than the usual 40 ms search.
_GLIDE_SLACK = 0.07
_MIN_RUN = 4
_HNR_ON = 2.0
_RATIO_ON = 1.2
_RMS_VOWEL = 0.40
_RMS_LEVEL = 0.62
_F1_LO = 180.0
_F1_HI = 1200.0
_SHAPE_ON = 0.10
# Nasal murmur sits in the lowest band. The glide and the vowel target both leave it.
_NASAL_LOW = 0.55


def refine_cv_boundary(
    mora: Token,
    coarse: float,
    start: float,
    end: float,
    track: FrameTrack,
    y: np.ndarray,
    sr: int,
    c_peak: float | None,
    harm: tuple[np.ndarray, np.ndarray] | None,
    formants: tuple[np.ndarray, np.ndarray, np.ndarray] | None,
) -> float:
    kind = _edge_kind(mora)
    if kind is None or end <= start + 0.04:
        return coarse
    glide = _palatal_glide(mora)
    slack = _GLIDE_SLACK if glide else _SLACK
    floor, ceiling = _limits(mora, kind, coarse, start, end, track, c_peak)
    search_lo = max(floor, coarse - slack)
    search_hi = min(ceiling, coarse + slack)
    if search_hi < search_lo + 0.012:
        return coarse
    times, rms, ratio, bands = local_edge_cues(y, sr, search_lo, search_hi)
    if len(times) < _MIN_RUN + 2:
        return coarse
    hnr = _nearest(harm[0], harm[1], times, 0.003) if harm is not None else None
    f1 = _nearest(formants[0], formants[1], times, 0.004) if formants is not None else None
    nucleus = _nucleus_mask(times, coarse)
    if kind == "obstruent":
        mask = _obstruent_mask(mora, rms, ratio, hnr, f1, nucleus, times, coarse, glide)
    elif kind == "nasal":
        mask = _glide_nasal_mask(bands) if glide else _nasal_mask(f1, bands, nucleus)
    else:
        mask = _level_mask(rms, nucleus)
    if mask is None:
        return coarse
    edge = _left_edge(times, mask, coarse)
    if edge is None:
        return coarse
    lo = max(floor, coarse - slack)
    hi = min(ceiling, coarse + slack)
    if hi <= lo:
        return coarse
    return float(min(max(edge, lo), hi))


def _palatal_glide(mora: Token) -> bool:
    """True when a y-glide sits between the consonant and the vowel target.

    しゃ・じゃ・きゃ are consonant, then y, then the vowel. し・ち・じ land on
    /i/, which is already the palatal target, so they are not this case.
    """
    cons = mora.consonant or ""
    if cons.endswith("y"):
        return True
    return cons in {"sh", "ch", "j"} and mora.vowel != "i"


def _edge_kind(mora: Token) -> str | None:
    if not mora.needs_cv_split:
        return None
    if mora.is_stop or mora.is_affricate or mora.is_fricative:
        return "obstruent"
    if mora.is_nasal:
        return "nasal"
    if mora.is_weak_fricative or mora.is_labial_fricative or mora.is_flap:
        return "level"
    return None


def _limits(
    mora: Token,
    kind: str,
    coarse: float,
    start: float,
    end: float,
    track: FrameTrack,
    c_peak: float | None,
) -> tuple[float, float]:
    if kind == "obstruent":
        return _release_floor(mora, track, start, end, c_peak), end - 0.015
    if kind == "nasal":
        return start + 0.02, end - 0.02
    if mora.is_labial_fricative:
        return start + 0.035, min(end - 0.03, start + 0.12)
    if mora.is_flap:
        return start + 0.018, min(end - 0.015, start + 0.06)
    return start + 0.03, min(end - 0.025, start + 0.20)


def _release_floor(
    mora: Token,
    track: FrameTrack,
    start: float,
    end: float,
    c_peak: float | None,
) -> float:
    """Earliest vowel time. The burst and the frication onset stay in the consonant."""
    if mora.is_stop:
        burst = _flux_peak(track, start, end)
        if burst is None:
            burst = c_peak if c_peak is not None else start
        return min(end - 0.02, max(start, burst) + 0.008)
    release = sibilant_release(track, start, end)
    if release is not None:
        return release
    return start + 0.02


def _flux_peak(track: FrameTrack, start: float, end: float) -> float | None:
    sel = (track.times >= start) & (track.times <= end)
    if int(np.count_nonzero(sel)) < 3:
        return None
    times = track.times[sel]
    flux = track.flux[sel]
    inner = slice(1, max(2, len(times) - 1))
    if len(times[inner]) == 0:
        return None
    return float(times[inner][int(np.argmax(flux[inner]))])


def _nearest(times: np.ndarray, values: np.ndarray, grid: np.ndarray, tol: float) -> np.ndarray:
    out = np.full(len(grid), np.nan, dtype=float)
    if times.size == 0 or values.size == 0:
        return out
    idx = np.searchsorted(times, grid, side="left")
    idx = np.clip(idx, 0, times.size - 1)
    prev = np.clip(idx - 1, 0, times.size - 1)
    take_prev = np.abs(times[prev] - grid) <= np.abs(times[idx] - grid)
    idx = np.where(take_prev, prev, idx)
    picked = np.asarray(values, dtype=float)[idx]
    ok = (np.abs(times[idx] - grid) <= tol) & np.isfinite(picked)
    out[ok] = picked[ok]
    return out


def _nucleus_mask(times: np.ndarray, coarse: float) -> np.ndarray:
    right = times >= coarse + 0.012
    if int(np.count_nonzero(right)) >= 4:
        return right
    right = times >= coarse - 1e-4
    if int(np.count_nonzero(right)) >= 3:
        return right
    sel = np.zeros(len(times), dtype=bool)
    sel[max(0, len(times) - 4) :] = True
    return sel


def _obstruent_mask(
    mora: Token,
    rms: np.ndarray,
    ratio: np.ndarray,
    hnr: np.ndarray | None,
    f1: np.ndarray | None,
    nucleus: np.ndarray,
    times: np.ndarray,
    coarse: float,
    glide: bool,
) -> np.ndarray | None:
    if mora.is_stop and (hnr is None or not np.any(np.isfinite(hnr))):
        return None
    nuc = float(np.median(rms[nucleus])) if np.any(nucleus) else float(np.median(rms))
    if nuc < 1e-5:
        return None
    voiced = np.ones(len(rms), dtype=bool)
    if hnr is not None and np.any(np.isfinite(hnr)):
        voiced = np.isfinite(hnr) & (hnr > _HNR_ON)
    elif mora.is_stop:
        return None
    # The glide is brighter than /a/ and its tracked F1 often sits on F2.
    # Gating on the vowel target waits until y becomes a. Full frication is
    # still brighter than the glide, so the fall off that peak is j → y.
    if glide:
        ratio_ok = ratio < _glide_ratio_limit(ratio, times, coarse)
        f1_ok = np.ones(len(rms), dtype=bool)
    else:
        ratio_ok = ratio < _RATIO_ON
        f1_ok = np.ones(len(rms), dtype=bool)
        if f1 is not None:
            finite = np.isfinite(f1)
            f1_ok = ~finite | ((f1 >= _F1_LO) & (f1 <= _F1_HI))
    return voiced & ratio_ok & (rms > _RMS_VOWEL * nuc) & f1_ok


def _glide_ratio_limit(ratio: np.ndarray, times: np.ndarray, coarse: float) -> float:
    """Ratio still accepted as the glide, above the vowel and below full frication.

    The window is mostly the glide when the coarse cut sits on y → a, so a
    median would describe y, not j. The top of the left side is the frication
    when it is actually in view. Without that bright peak, ratio is not used.
    """
    left = times < coarse - 0.005
    if int(np.count_nonzero(left)) < 3:
        return 1e6
    peak = float(np.percentile(ratio[left], 90))
    if peak < 2.2:
        return 1e6
    # No fixed ceiling. The glide can sit well above the 1.2 vowel line and
    # still be several times quieter, in the high band, than the frication.
    return float(max(_RATIO_ON, 0.70 * peak))


def _glide_nasal_mask(bands: np.ndarray) -> np.ndarray | None:
    """Oral onset, including the y glide. Matching the vowel target would cut at y → a."""
    if bands.ndim != 2 or len(bands) < _MIN_RUN or bands.shape[1] < 1:
        return None
    return bands[:, 0] <= _NASAL_LOW


def _nasal_mask(
    f1: np.ndarray | None,
    bands: np.ndarray,
    nucleus: np.ndarray,
) -> np.ndarray | None:
    n = len(nucleus)
    f1_close = np.ones(n, dtype=bool)
    have_f1 = False
    if f1 is not None:
        nuc_f1 = f1[nucleus & np.isfinite(f1)]
        if nuc_f1.size >= 3:
            have_f1 = True
            center = float(np.median(nuc_f1))
            tol = max(90.0, 0.22 * center)
            finite = np.isfinite(f1)
            f1_close = ~finite | (np.abs(f1 - center) <= tol)
    shape_close = np.ones(n, dtype=bool)
    have_shape = False
    if bands.ndim == 2 and len(bands) == n and np.any(nucleus):
        nuc_shape = np.mean(bands[nucleus], axis=0)
        norm = float(np.linalg.norm(nuc_shape))
        if norm > 1e-8:
            have_shape = True
            nuc_shape = nuc_shape / norm
            row_norm = np.linalg.norm(bands, axis=1)
            sim = np.zeros(n, dtype=float)
            ok = row_norm > 1e-8
            sim[ok] = bands[ok] @ nuc_shape / row_norm[ok]
            shape_close = (1.0 - np.clip(sim, -1.0, 1.0)) <= _SHAPE_ON
    if not have_f1 and not have_shape:
        return None
    return f1_close & shape_close


def _level_mask(rms: np.ndarray, nucleus: np.ndarray) -> np.ndarray | None:
    held = rms[nucleus] if np.any(nucleus) else rms
    nuc = float(np.median(held))
    if nuc < 1e-5:
        return None
    return rms >= _RMS_LEVEL * nuc


def _left_edge(times: np.ndarray, mask: np.ndarray, coarse: float) -> float | None:
    """Start of the vowel run that reaches the nucleus, when a consonant precedes it.

    A short voiced blip before the vowel does not qualify: the run has to
    overlap the stable region just after the coarse cut.
    """
    filled = close_short_gaps(np.asarray(mask, dtype=bool), 1)
    vowel_runs = [(start, end) for start, end in runs(filled) if end - start >= _MIN_RUN]
    n = len(filled)
    if not vowel_runs:
        return None
    # The run that covers the vowel just after the coarse cut. A later run,
    # such as the next mora, must not steal the boundary.
    nucleus_i = int(np.searchsorted(times, min(coarse + 0.02, float(times[-1])), side="left"))
    nucleus_i = min(max(nucleus_i, 0), n - 1)
    containing = [run for run in vowel_runs if run[0] <= nucleus_i < run[1]]
    if containing:
        start_i = containing[0][0]
    else:
        coarse_i = int(np.searchsorted(times, coarse, side="left"))
        later = [run for run in vowel_runs if run[0] >= coarse_i]
        if not later:
            return None
        start_i = max(later, key=lambda run: run[1] - run[0])[0]
    if start_i == 0:
        return None
    gap_end = start_i - 1
    while gap_end >= 0 and not filled[gap_end]:
        gap_end -= 1
    # A hole surrounded by vowel is a dropout. The consonant is the open
    # stretch from the start of the window, or a gap at least 8 ms long.
    if gap_end >= 0 and (start_i - gap_end - 1) < _MIN_RUN:
        return None
    return float(times[start_i])
