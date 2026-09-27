from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from otoasobi.features import FrameTrack, close_short_gaps, runs
from otoasobi.parser import MoraGroup, Token, rest_layout, speech_chunks, speech_group_chunks
from otoasobi.phones import SILENCE_LABEL, phone_kind, phone_name


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


def _smooth(values: np.ndarray, width: int) -> np.ndarray:
    if width <= 1 or len(values) < width:
        return np.asarray(values, dtype=float)
    kernel = np.ones(width) / width
    return np.convolve(np.asarray(values, dtype=float), kernel, mode="same")


def split_leading_breath(
    track: FrameTrack,
    start: float,
    end: float,
) -> tuple[float, float] | None:
    """Split an inhale off the front of a speech island.

    A loud breath clears the same energy gate as speech, so the first island
    starts at the inhale and the first phone is cut inside that noise. The
    inhale is an unvoiced hump, then a valley, then the syllable. Returns
    ``(breath_end, speech_start)``: the valley begins at ``breath_end``, and
    ``speech_start`` is the rise after it. ``None`` when there is no separate
    leading breath.
    """
    if end - start < 0.16:
        return None
    sel = (track.times >= start) & (track.times <= end)
    times = track.times[sel]
    if len(times) < 12:
        return None
    rms = _smooth(track.rms[sel], 3)
    zcr = _smooth(track.zcr[sel], 3)
    dt = float(np.median(np.diff(times))) if len(times) > 1 else 0.01
    peak = float(np.max(rms))
    if peak < 1e-4:
        return None
    voiced = zcr < 0.09
    if int(np.count_nonzero(voiced)) < 4:
        return None
    vpeak = float(np.max(rms[voiced]))
    if vpeak < peak * 0.12:
        return None
    body_thr = vpeak * 0.45
    body: int | None = None
    need = max(3, int(round(0.03 / max(dt, 1e-3))))
    for i in range(0, len(rms) - need):
        if float(np.min(rms[i : i + need])) >= body_thr and bool(np.all(voiced[i : i + need])):
            body = i
            break
    if body is None or float(times[body]) < start + 0.10:
        return None
    min_i = int(np.searchsorted(times, start + 0.07, side="left"))
    max_i = body - max(2, int(round(0.03 / max(dt, 1e-3))))
    if max_i <= min_i:
        return None
    trough_thr = vpeak * 0.32
    best: int | None = None
    for i in range(min_i, max_i):
        if float(rms[i]) > trough_thr:
            continue
        wing = max(2, int(round(0.02 / max(dt, 1e-3))))
        left = max(0, i - wing)
        right = min(len(rms), i + wing + 1)
        if float(rms[i]) > float(np.min(rms[left:right])) + peak * 0.02:
            continue
        # Compare with the hump immediately before this frame. A longer
        # window still sees the inhale from inside the consonant and treats
        # the consonant as the valley.
        look = max(2, int(round(0.04 / max(dt, 1e-3))))
        left_peak = float(np.max(rms[max(0, i - look) : i])) if i > 0 else 0.0
        if left_peak < 1e-5 or float(rms[i]) > left_peak * 0.55:
            continue
        pre = rms[:i]
        hump = float(np.max(pre))
        if hump < max(vpeak * 0.10, peak * 0.08):
            continue
        if float(rms[i]) > hump * 0.62:
            continue
        hot = pre >= hump * 0.45
        if float(np.count_nonzero(hot)) * dt < 0.08:
            continue
        if float(np.median(zcr[:i][hot])) < 0.10:
            continue
        best = i
    if best is None:
        return None
    # Stay under a quiet initial fricative so [ɸ] is not left in the gap.
    quiet = max(float(rms[best]) * 1.35, vpeak * 0.05)
    gap_i = best
    while gap_i > 0 and float(rms[gap_i - 1]) <= quiet:
        gap_i -= 1
    rise_i = best
    while rise_i < body and float(rms[rise_i]) <= quiet:
        rise_i += 1
    if (float(times[rise_i]) - float(times[gap_i])) < 0.02:
        return None
    breath_end = float(times[gap_i])
    speech_start = float(times[min(rise_i, len(times) - 1)])
    if speech_start <= breath_end + 0.015 or speech_start >= float(times[body]):
        return None
    if breath_end <= start + 0.05:
        return None
    return breath_end, speech_start


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
        if not moras:
            continue
        lo = 0.0 if i == 0 else islands[i - 1][1]
        if i == 0:
            # A leading inhale is its own hump. Keep the consonant after the
            # valley, and let the onset walk stop at the breath instead of
            # stepping back into it.
            split = split_leading_breath(track, islands[i][0], islands[i][1])
            if split is not None:
                breath_end, speech_start = split
                lo = max(lo, breath_end)
                islands[i] = (max(islands[i][0], speech_start), islands[i][1])
        if moras[0].is_stop or moras[0].is_affricate:
            islands[i] = (stop_closure_start(track, islands[i][0], lo), islands[i][1])
        elif moras[0].is_weak_fricative:
            islands[i] = (weak_fricative_onset(track, islands[i][0], lo), islands[i][1])
    for i in range(1, len(islands)):
        prev_end = islands[i - 1][1]
        start, end = islands[i]
        if start < prev_end:
            start = prev_end
        if end < start + 0.02:
            end = min(duration, start + 0.02)
        islands[i] = (start, end)
    return islands


def _fricative_onsets(
    track: FrameTrack, start: float, end: float, count: int, ratio_thr: float = 1.8
) -> list[float] | None:
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
    fric = (ratio > ratio_thr) & (rms > speech_thr * 0.25)
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


def _scan_sibilant(track: FrameTrack, start: float, end: float) -> tuple[float | None, float | None]:
    """Frication start and the following vowel onset, skipping a leading closure.

    Returns ``(release, fall)``. ``fall`` is None when the high band never
    drops. A quiet closure can sit under the 1.2 ratio line, or sit on the
    noise floor with an unstable ratio; either way it is not the vowel.
    """
    sel = (track.times >= start) & (track.times <= end)
    if int(np.count_nonzero(sel)) < 4:
        return None, None
    times = track.times[sel]
    ratio = track.ratio[sel]
    rms = track.rms[sel]
    audible = rms > _speech_thr(track) * 0.2
    fric = audible & (ratio >= 1.2)
    seen = False
    hi_run = 0
    lo_run = 0
    release_i: int | None = None
    first_low: int | None = None
    run_start = 0
    for i in range(len(times)):
        if bool(fric[i]):
            if hi_run == 0:
                run_start = i
            hi_run += 1
            lo_run = 0
            first_low = None
            if hi_run >= 2 and release_i is None:
                seen = True
                release_i = run_start
            continue
        hi_run = 0
        if not seen:
            continue
        lo_run += 1
        if first_low is None:
            first_low = i
        if lo_run >= 2 and release_i is not None:
            return float(times[release_i]), float(times[i - lo_run + 1])
    release_t = float(times[release_i]) if release_i is not None else None
    if first_low is not None and float(np.median(ratio[first_low:])) < 1.2:
        return release_t, float(times[first_low])
    return release_t, None


def sibilant_release(track: FrameTrack, start: float, end: float) -> float | None:
    """First audible frame of the high band above 3.5 kHz."""
    release, _fall = _scan_sibilant(track, start, end)
    return release


def sibilant_vowel_start(track: FrameTrack, start: float, end: float) -> float:
    """Vowel onset after [s]/[ts]/[tɕ]. The cut is the fall after the release.

    The 25 ms analysis window starts losing the high band before a short
    affricate burst has actually ended, so a release shorter than 50 ms
    keeps one extra frame.
    """
    span = end - start
    if span < 0.04:
        return start + span * 0.35
    release, fall = _scan_sibilant(track, start, end)
    if fall is None:
        cut = start + span * 0.28
        if release is not None:
            cut = max(cut, min(end - 0.015, release + 0.02))
        return cut
    cut = fall
    if release is not None and fall - release <= 0.05:
        cut = min(end - 0.015, fall + 0.01)
    return cut


def affricate_closure_start(track: FrameTrack, burst: float, t_lo: float, max_closure: float = 0.12) -> float:
    """Start of the silent closure before an affricate release.

    The stop walker treats anything quieter than a vowel as closure. Mora
    nasal ん is quieter than that, so its tail was labeled ts. Only frames
    near the noise floor belong to the closure; audible low-frequency energy
    is still the nasal.
    """
    t_lo = max(0.0, t_lo)
    burst = max(burst, t_lo + 0.01)
    min_t = max(t_lo, burst - max_closure)
    sel = (track.times >= min_t) & (track.times <= burst + 0.01)
    if int(np.count_nonzero(sel)) < 3:
        return min_t
    times = track.times[sel]
    rms = track.rms[sel]
    centroid = track.centroid[sel]
    ratio = track.ratio[sel]
    peak_i = int(np.argmin(np.abs(times - burst)))
    speech_thr = _speech_thr(track)
    quiet = speech_thr * 1.15
    nasal_floor = speech_thr * 0.55
    i = peak_i
    # The anchor may sit inside the burst. Leave that high band before
    # measuring the closure, or the loud release stops the walk immediately.
    while i > 0 and float(ratio[i - 1]) >= 1.2 and float(rms[i - 1]) >= quiet:
        i -= 1
    while i > 0:
        prev_rms = float(rms[i - 1])
        if prev_rms >= quiet:
            break
        if prev_rms >= nasal_floor and float(centroid[i - 1]) < 1500.0 and float(ratio[i - 1]) < 1.2:
            break
        i -= 1
    return max(min_t, float(times[i]))


def _strongest_release(track: FrameTrack, start: float, end: float) -> float | None:
    """Start of the loudest high-band run in the span.

    A vowel can host a brief ratio bump. The affricate burst is the run whose
    energy above 3.5 kHz is actually strong, so a weak early bump loses.
    """
    sel = (track.times >= start) & (track.times <= end)
    if int(np.count_nonzero(sel)) < 4:
        return None
    times = track.times[sel]
    ratio = track.ratio[sel]
    audible = track.rms[sel] > _speech_thr(track) * 0.2
    fric = audible & (ratio >= 1.2)
    best_score = 1.8
    best_at: float | None = None
    i = 0
    n = len(times)
    while i < n:
        if not bool(fric[i]):
            i += 1
            continue
        j = i + 1
        while j < n and bool(fric[j]):
            j += 1
        if j - i >= 2:
            score = float(np.max(ratio[i:j]))
            if score >= best_score:
                best_score = score
                best_at = float(times[i])
        i = j
    return best_at


def affricate_onset(
    track: FrameTrack,
    peak: float,
    t_lo: float,
    t_hi: float,
    vowel_at: float | None = None,
) -> float:
    """Start of [ts]/[tɕ]/[dʑ], including the closure in front of the release.

    ``peak`` is an aligned consonant time. For つ that is the [t] letter,
    and it often falls inside the preceding vowel, well before the [s].
    The burst is the strongest high band before this mora's vowel, not the
    first bump within 150 ms of that early [t]. The walk then stops at the
    vowel or at ん, and does not treat either as closure.
    """
    t_lo = max(0.0, t_lo)
    left = max(t_lo, peak - 0.03)
    if vowel_at is not None and vowel_at > peak + 0.04:
        right = min(t_hi, vowel_at + 0.04)
    else:
        right = min(t_hi, peak + 0.36)
    if right < left + 0.05:
        right = min(t_hi, left + 0.12)
    release = _strongest_release(track, left, right)
    farther = min(t_hi, max(peak, t_lo) + 0.36)
    if release is None and farther > right + 0.03:
        release = _strongest_release(track, left, farther)
    burst = release if release is not None else max(peak, t_lo + 0.012)
    burst = max(burst, t_lo + 0.012)
    return affricate_closure_start(track, burst, t_lo, max_closure=0.12)


def _affricate_onsets(track: FrameTrack, start: float, end: float, count: int) -> list[float] | None:
    """Mora onsets for a つ/ち/じ run. Each onset includes that mora’s closure."""
    releases = _fricative_onsets(track, start, end, count)
    if releases is None:
        releases = _fricative_onsets(track, start, end, count, ratio_thr=1.25)
    if releases is None:
        return None
    onsets: list[float] = []
    for i, release in enumerate(releases):
        t_lo = start if i == 0 else onsets[-1] + 0.045
        if release <= t_lo + 0.012:
            onsets.append(max(t_lo, release))
        else:
            onsets.append(affricate_closure_start(track, release, t_lo, 0.12))
    if onsets:
        onsets[0] = start
    return onsets


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


def labial_onset(track: FrameTrack, peak: float, t_lo: float, t_hi: float) -> float:
    """Start of ヴ: the rise out of silence, or the drop into the constriction."""
    t_lo = max(0.0, t_lo)
    peak = min(max(peak, t_lo), max(t_lo, t_hi))
    lo = max(t_lo, peak - 0.10)
    hi = min(t_hi, max(peak + 0.02, lo + 0.03))
    sel = (track.times >= lo) & (track.times <= hi)
    if int(np.count_nonzero(sel)) < 3:
        return max(t_lo, peak - 0.04)
    times = track.times[sel]
    rms = track.rms[sel]
    if len(times) < 3:
        return max(t_lo, peak - 0.04)
    speech_thr = _speech_thr(track)
    if float(rms[0]) < speech_thr * 1.5:
        i = 0
        while i < len(rms) - 1 and float(rms[i]) < speech_thr * 0.45:
            i += 1
        return max(t_lo, float(times[i]))
    i = int(np.argmin(np.diff(rms)))
    return max(t_lo, float(times[i]))


def labial_vowel_start(track: FrameTrack, start: float, end: float) -> float:
    """Vowel onset after ヴ.

    [β] stays weak and low-frequency, so a high-band ratio cut marks the
    consonant as a single frame. The vowel is the rise in level, or in
    centroid when the vowel is more open.
    """
    span = end - start
    if span < 0.05:
        return start + span * 0.4
    hi = min(end - 0.03, start + 0.12)
    if hi <= start + 0.04:
        return start + span * 0.4
    sel = (track.times >= start) & (track.times <= hi)
    if int(np.count_nonzero(sel)) < 4:
        return min(start + 0.055, end - 0.03)
    times = track.times[sel]
    kernel = np.ones(3) / 3.0
    rms = np.convolve(track.rms[sel], kernel, mode="same")
    cen = np.convolve(track.centroid[sel], kernel, mode="same")
    rms_d = np.diff(rms)
    cen_d = np.diff(cen)
    rms_scale = float(np.percentile(np.abs(rms_d), 80)) + 1e-8
    cen_scale = float(np.percentile(np.abs(cen_d), 80)) + 1e-8
    score = np.maximum(rms_d, 0.0) / rms_scale + 0.5 * np.maximum(cen_d, 0.0) / cen_scale
    best_i: int | None = None
    best = 0.0
    for i, value in enumerate(score):
        if float(times[i + 1]) < start + 0.03:
            continue
        if float(value) > best:
            best = float(value)
            best_i = i + 1
    fallback = start + min(0.055, span * 0.4)
    if best_i is None:
        cut = fallback
    else:
        rise = float(rms[best_i] - rms[best_i - 1])
        level = float(np.median(rms)) + 1e-8
        cen_rise = float(cen[best_i] - cen[best_i - 1])
        if rise < 0.18 * level and cen_rise < 200.0:
            cut = fallback
        else:
            cut = float(times[best_i])
    lo = start + 0.035
    hi_cut = min(end - 0.03, start + 0.11)
    if hi_cut <= lo:
        return start + span * 0.4
    return min(max(cut, lo), hi_cut)


def weak_fricative_onset(track: FrameTrack, edge: float, t_lo: float, t_hi: float | None = None) -> float:
    """Start of [h]/[ç]/[ɸ] when the RMS gate only marked the vowel.

    The consonant is quieter than the vowel and sits against it, so it falls
    under the speech threshold and the row is analyzed as if it were not there.
    Walk back from the vowel edge across that quieter region, and stop at
    silence or at the previous vowel.
    """
    edge = float(edge)
    t_lo = max(0.0, float(t_lo))
    earliest = max(t_lo, edge - 0.20)
    if edge <= earliest + 0.015:
        return max(t_lo, edge)
    far_hi = edge + 0.14
    if t_hi is not None:
        far_hi = min(far_hi, float(t_hi))
    near = (track.times >= edge) & (track.times <= edge + 0.04)
    far = (track.times >= edge + 0.06) & (track.times <= far_hi)
    if int(np.count_nonzero(near)) < 2 and int(np.count_nonzero(far)) < 2:
        return max(t_lo, edge)
    levels = []
    if np.any(near):
        levels.append(float(np.median(track.rms[near])))
    if np.any(far):
        levels.append(float(np.median(track.rms[far])))
    vowel = max(levels) if levels else 0.0
    if vowel < 1e-5:
        return max(t_lo, edge)
    # A low percentile stays in the pad. Percentile 8 often sits inside [h]
    # itself, and a threshold taken from there blocks the walk on the first
    # frame.
    floor = float(np.percentile(track.rms, 3))
    thr = max(floor * 2.0, vowel * 0.15, 1e-4)
    cap = vowel * 0.72
    if cap <= thr:
        return max(t_lo, edge)
    sel = (track.times >= earliest) & (track.times <= edge + 0.01)
    times = track.times[sel]
    rms = track.rms[sel]
    if len(times) < 2:
        return max(t_lo, edge)
    i = int(np.argmin(np.abs(times - edge)))
    while i > 0 and thr < float(rms[i - 1]) < cap:
        i -= 1
    while i < len(times) - 1 and float(rms[i]) <= thr and float(times[i]) < edge:
        i += 1
    return max(t_lo, min(edge, float(times[i])))


def weak_fricative_vowel_start(track: FrameTrack, start: float, end: float) -> float:
    """Vowel onset after は行 / ふ行.

    [h], [ç], and [ɸ] do not put their energy above 3.5 kHz, so a high/low
    ratio cut treats the first frame as the vowel. The consonant is the
    quieter, noisier stretch before the level rises into the vowel.
    """
    span = end - start
    if span < 0.05:
        return start + span * 0.45
    hi = min(end - 0.025, start + 0.20)
    lo = start + 0.03
    if hi <= lo + 0.015:
        return start + min(0.05, span * 0.4)
    sel = (track.times >= start) & (track.times <= hi)
    times = track.times[sel]
    if len(times) < 4:
        return start + min(0.06, span * 0.4)
    kernel = np.ones(3) / 3.0
    rms = np.convolve(track.rms[sel], kernel, mode="same")
    zcr = np.convolve(track.zcr[sel], kernel, mode="same")
    peak = float(np.max(rms)) + 1e-8
    zpeak = float(np.max(zcr)) + 1e-8
    best_i: int | None = None
    best = -1.0
    for i in range(1, len(times) - 2):
        if float(times[i]) < lo:
            continue
        before = float(np.mean(rms[max(0, i - 3) : i]))
        after = float(np.mean(rms[i : i + 3]))
        rise = (after - before) / peak
        zb = float(np.mean(zcr[max(0, i - 3) : i]))
        za = float(np.mean(zcr[i : i + 3]))
        # The vowel is louder. A drop into a following gap is not the boundary,
        # and entering the fricative from silence raises the zero-crossing rate.
        if rise < 0.08:
            continue
        if za > zb * 1.15 and za > zb + 0.01:
            continue
        zdrop = max(0.0, (zb - za) / zpeak)
        score = max(rise, 0.0) + 0.55 * zdrop
        if score > best:
            best = score
            best_i = i
    head = float(np.median(rms[: max(2, min(3, len(rms)))]))
    tail = float(np.median(rms[max(0, len(rms) // 3) :])) + 1e-8
    if best_i is None or best < 0.12 or (head > 0.75 * tail and best < 0.2):
        cut = start + min(0.045, span * 0.35)
    else:
        cut = float(times[best_i])
    hi_cut = min(end - 0.025, start + 0.20)
    if hi_cut <= lo:
        return start + span * 0.4
    return min(max(cut, lo), hi_cut)


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


def _weak_fricative_onsets(track: FrameTrack, start: float, end: float, count: int) -> list[float] | None:
    """Mora onsets for a は行 / ふ行 run: each consonant is a drop into quieter noise."""
    if count <= 0:
        return []
    if count == 1:
        return [start]
    sel = (track.times >= start) & (track.times <= end)
    if int(np.count_nonzero(sel)) < count * 4:
        return None
    times = track.times[sel]
    if len(times) < 6:
        return None
    rms = np.convolve(track.rms[sel], np.ones(3) / 3.0, mode="same")
    zcr = np.convolve(track.zcr[sel], np.ones(3) / 3.0, mode="same")
    drop = np.maximum(0.0, -np.diff(rms))
    zrise = np.maximum(0.0, np.diff(zcr))
    score = drop / (float(np.max(drop)) + 1e-8) + 0.45 * zrise / (float(np.max(zrise)) + 1e-8)
    at = times[1:]
    score = score.copy()
    score[at < start + 0.08] = 0.0
    score[at > end - 0.06] = 0.0
    if float(np.max(score)) < 0.35:
        return None
    peaks = _spaced_peaks(at, score, count - 1, min_dt=0.12)
    if peaks is None:
        return None
    return [start, *peaks]


def _labial_onsets(track: FrameTrack, start: float, end: float, count: int) -> list[float] | None:
    sel = (track.times >= start) & (track.times <= end)
    if not np.any(sel):
        return None
    times = track.times[sel]
    rms = track.rms[sel]
    score = np.zeros(len(rms))
    if len(rms) >= 3:
        score[1:-1] = 0.5 * (rms[:-2] + rms[2:]) - rms[1:-1]
    peaks = _spaced_peaks(times, score, count, min_dt=0.12)
    if peaks is None:
        return None
    onsets = [labial_onset(track, peak, start if i == 0 else peaks[i - 1], end) for i, peak in enumerate(peaks)]
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


def _cosine_distance(a: np.ndarray, b: np.ndarray) -> float:
    na = float(np.linalg.norm(a))
    nb = float(np.linalg.norm(b))
    if na < 1e-8 or nb < 1e-8:
        return 0.0
    sim = float(np.dot(a, b) / (na * nb))
    if sim > 1.0:
        sim = 1.0
    elif sim < -1.0:
        sim = -1.0
    return 1.0 - sim


def _smooth_envelope(rows: np.ndarray) -> np.ndarray:
    if len(rows) < 3:
        return rows
    padded = np.pad(rows, ((1, 1), (0, 0)), mode="edge")
    kernel = np.array([0.25, 0.5, 0.25])
    out = np.empty_like(rows)
    for k in range(rows.shape[1]):
        out[:, k] = np.convolve(padded[:, k], kernel, mode="valid")
    norms = np.linalg.norm(out, axis=1, keepdims=True)
    return out / np.maximum(norms, 1e-8)


def _envelope_boundary(track: FrameTrack, t0: float, t1: float) -> float | None:
    """Time that best separates the two level-free mel envelopes in ``[t0, t1]``.

    Each frame already has its gain removed. The score is the cosine distance
    between the mean shape on the left of a candidate cut and the mean shape
    on the right, so a loudness change with the same vowel does not score.
    Returns None when that separation is too weak to move a boundary.
    """
    if t1 - t0 < 0.05 or track.envelope.ndim != 2 or len(track.envelope) != len(track.times):
        return None
    sel = (track.times >= t0) & (track.times <= t1)
    if int(np.count_nonzero(sel)) < 6:
        return None
    times = track.times[sel]
    shape = _smooth_envelope(track.envelope[sel])
    n = len(times)
    scores: list[float] = []
    cuts: list[float] = []
    for i in range(2, n - 1):
        if float(times[i - 1] - times[0]) < 0.02 or float(times[-1] - times[i]) < 0.02:
            continue
        dist = _cosine_distance(np.mean(shape[:i], axis=0), np.mean(shape[i:], axis=0))
        scores.append(dist)
        cuts.append(0.5 * (float(times[i - 1]) + float(times[i])))
    if len(scores) < 3:
        return None
    score = np.asarray(scores, dtype=float)
    peak = float(np.max(score))
    # Close vowels such as /u/-/o/ separate by about 0.05. A repeated vowel,
    # even with noise and a loudness change, stays under about 0.01.
    if peak < 0.03:
        return None
    baseline = float(np.percentile(score, 40))
    if peak < max(0.03, baseline * 1.55):
        return None
    peak_i = int(np.argmax(score))
    thr = peak * 0.85
    left = peak_i
    while left > 0 and float(score[left - 1]) >= thr:
        left -= 1
    right = peak_i
    while right < len(score) - 1 and float(score[right + 1]) >= thr:
        right += 1
    return float(cuts[(left + right) // 2])


def refine_vowel_hiatus(
    track: FrameTrack,
    prior: float,
    center_a: float,
    center_b: float,
    same_vowel: bool,
    formants: tuple[np.ndarray, np.ndarray, np.ndarray] | None = None,
) -> float:
    """Nudge an MMS vowel boundary between the two vowel centers.

    ``prior`` is the start of the second vowel. Different vowels move to the
    cut that separates mel-envelope shape, ignoring level. A weak shape change
    keeps that time. Same vowels move only toward a clear RMS dip near it.
    """
    lo, hi = (center_a, center_b) if center_a <= center_b else (center_b, center_a)
    if hi <= lo:
        return prior
    prior = min(max(prior, lo), hi)
    if hi - lo < 0.02:
        return prior
    if same_vowel:
        return _rms_dip_near(track, prior, lo, hi)
    moved = _envelope_boundary(track, lo, hi)
    if moved is not None:
        return moved
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
    moved = _envelope_boundary(track, t0, t1)
    if moved is not None:
        return moved
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
    if all(m.is_labial_fricative and m.needs_cv_split for m in moras):
        onsets = _labial_onsets(track, start, end, n)
        if onsets is not None:
            return _onsets_to_bounds(onsets, start, end)
    if all(m.is_weak_fricative and m.needs_cv_split for m in moras):
        onsets = _weak_fricative_onsets(track, start, end, n)
        if onsets is not None:
            return _onsets_to_bounds(onsets, start, end)
    if all(m.is_affricate and m.needs_cv_split for m in moras):
        onsets = _affricate_onsets(track, start, end, n)
        if onsets is not None:
            return _onsets_to_bounds(onsets, start, end)
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
    return _refine_vowel_sequences(track, moras, _equal_bounds(start, end, n))


def _sonorant_join(prev: Token, mora: Token) -> bool:
    """True when a vowel or ん meets a vowel, with no consonant between them.

    「かお」「あん」「んあ」 qualify. 「んか」 and 「んな」 do not: the next
    mora starts with a consonant, so that edge stays a consonant onset.
    """
    if mora.is_vowel_only and (prev.vowel is not None or prev.kind == "N"):
        return True
    return mora.kind == "N" and prev.vowel is not None


def _refine_vowel_sequences(
    track: FrameTrack,
    moras: list[Token],
    bounds: list[tuple[float, float]],
) -> list[tuple[float, float]]:
    """Move a mora boundary where a vowel or ん continues without a consonant.

    「かお」 and 「かん」 keep the consonant onset, then place the following
    vowel or ん where the mel envelope changes. 「んか」 is left alone.
    """
    if len(bounds) != len(moras):
        return bounds
    bounds = list(bounds)
    for i in range(1, len(moras)):
        prev = moras[i - 1]
        mora = moras[i]
        if not _sonorant_join(prev, mora):
            continue
        a0, a1 = bounds[i - 1]
        b0, b1 = bounds[i]
        if prev.kind == "N" or prev.is_vowel_only:
            center_a = 0.5 * (a0 + a1)
        else:
            center_a = a0 + 0.65 * (a1 - a0)
        center_b = b0 + (0.45 if mora.kind == "N" else 0.55) * (b1 - b0)
        if center_b <= center_a:
            continue
        onset = refine_vowel_hiatus(
            track,
            b0,
            center_a,
            center_b,
            prev.vowel is not None and prev.vowel == mora.vowel,
            None,
        )
        onset = min(max(onset, a0 + 0.04), b1 - 0.04)
        if onset <= a0 + 0.02 or onset >= b1 - 0.02:
            continue
        bounds[i - 1] = (a0, onset)
        bounds[i] = (onset, b1)
    return bounds


def _cv_cut(track: FrameTrack, mora: Token, start: float, end: float) -> float:
    if end - start < 0.04:
        return start + (end - start) * 0.35
    if mora.is_weak_fricative:
        cut = weak_fricative_vowel_start(track, start, end)
    elif mora.is_fricative:
        cut = sibilant_vowel_start(track, start, end)
    elif mora.is_labial_fricative:
        cut = labial_vowel_start(track, start, end)
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


def split_vowel_time(parts: tuple[Token, ...], start: float, end: float) -> list[Span]:
    """Split one analyzed vowel into equal time slices, one per original mora."""
    n = len(parts)
    if n <= 0:
        return []
    if end < start:
        end = start
    if n == 1:
        part = parts[0]
        return [Span(start, end, phone_name(part, "V"), part.text)]
    step = (end - start) / n
    spans: list[Span] = []
    for i, part in enumerate(parts):
        a = start + i * step
        b = end if i == n - 1 else start + (i + 1) * step
        spans.append(Span(a, b, phone_name(part, "V"), part.text))
    return spans


def label_grouped_mora(
    group: MoraGroup,
    start: float,
    end: float,
    label_head,
) -> list[Span]:
    """Label a mora, then divide a merged same-vowel run into equal durations."""
    if len(group.parts) <= 1:
        return label_head(group.head, start, end)
    if group.head.is_vowel_only:
        return split_vowel_time(group.parts, start, end)
    pieces = label_head(group.head, start, end)
    if not pieces or phone_kind(pieces[-1].label) != "母音":
        return pieces
    vowel = pieces[-1]
    return pieces[:-1] + split_vowel_time(group.parts, vowel.start, vowel.end)


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
    chunks = speech_group_chunks(tokens)
    leading_rest, trailing_rest, internal_rests = rest_layout(tokens)
    chunk_bounds = chunk_time_bounds(tokens, track, duration)
    speech_start = chunk_bounds[0][0] if chunk_bounds else 0.0

    def label_head(mora: Token, start: float, end: float) -> list[Span]:
        return _label_mora(mora, start, end, track)

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

    for i, groups in enumerate(chunks):
        c0, c1 = chunk_bounds[i]
        heads = [group.head for group in groups]
        mora_bounds = _mora_bounds(track, heads, c0, c1)
        for group, (m0, m1) in zip(groups, mora_bounds, strict=True):
            spans.extend(label_grouped_mora(group, m0, m1, label_head))
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
