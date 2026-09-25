from __future__ import annotations

import numpy as np


def available() -> bool:
    try:
        import parselmouth  # noqa: F401
    except Exception:
        return False
    return True


def _sound(y: np.ndarray, sr: int):
    import parselmouth

    mono = np.asarray(y, dtype=np.float64)
    if mono.ndim > 1:
        mono = np.mean(mono, axis=1)
    return parselmouth.Sound(mono, sampling_frequency=float(sr))


def speech_bounds(y: np.ndarray, sr: int, duration: float) -> tuple[float, float]:
    snd = _sound(y, sr)
    intensity = snd.to_intensity(minimum_pitch=75.0, time_step=0.01)
    times = np.asarray(intensity.xs())
    vals = np.asarray(intensity.values[0], dtype=float)
    if times.size == 0:
        return 0.0, duration
    peak = float(np.max(vals))
    noise = float(np.percentile(vals, 10))
    thr = max(peak - 30.0, noise + 12.0, 40.0)
    mask = vals > thr
    if not np.any(mask):
        return 0.0, duration
    start_i = int(np.argmax(mask))
    end_i = int(len(mask) - 1 - np.argmax(mask[::-1]))
    start = float(times[start_i])
    end = min(duration, float(times[end_i]) + 0.01)
    return start, end


def harmonicity(y: np.ndarray, sr: int) -> tuple[np.ndarray, np.ndarray]:
    snd = _sound(y, sr)
    harm = snd.to_harmonicity_cc(time_step=0.005)
    times = np.asarray(harm.xs())
    vals = np.asarray(harm.values[0], dtype=float)
    vals = np.where(vals < -50.0, np.nan, vals)
    return times, vals


def voicing_onset(
    times: np.ndarray,
    vals: np.ndarray,
    t0: float,
    t1: float,
    fallback: float,
) -> float:
    sel = (times >= t0 + 0.012) & (times <= t1)
    if not np.any(sel):
        return fallback
    t = times[sel]
    v = vals[sel]
    ok = np.isfinite(v) & (v > 2.0)
    if not np.any(ok):
        return fallback
    run = 2
    for i in range(0, len(ok) - run):
        if bool(np.all(ok[i : i + run + 1])):
            cut = float(t[i])
            if t0 + 0.015 < cut < t1 - 0.015:
                return cut
            break
    return fallback


def formant_tracks(y: np.ndarray, sr: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Times, F1, and F2 from Burg tracking. Missing frames are NaN."""
    snd = _sound(y, sr)
    formant = snd.to_formant_burg(
        time_step=0.01,
        max_number_of_formants=5.0,
        maximum_formant=5500.0,
        window_length=0.025,
        pre_emphasis_from=50.0,
    )
    t0 = float(formant.start_time)
    t1 = float(formant.end_time)
    times = np.arange(t0, t1, 0.01)
    f1 = np.empty(len(times), dtype=float)
    f2 = np.empty(len(times), dtype=float)
    for i, t in enumerate(times):
        a = formant.get_value_at_time(1, float(t))
        b = formant.get_value_at_time(2, float(t))
        f1[i] = float(a) if a is not None and np.isfinite(a) else np.nan
        f2[i] = float(b) if b is not None and np.isfinite(b) else np.nan
    return times, f1, f2
