from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from numpy.fft import rfft, rfftfreq


@dataclass
class FrameTrack:
    sr: int
    hop: int
    times: np.ndarray
    rms: np.ndarray
    ratio: np.ndarray
    zcr: np.ndarray
    flux: np.ndarray


def _to_mono(y: np.ndarray) -> np.ndarray:
    y = np.asarray(y, dtype=np.float64)
    if y.ndim > 1:
        y = np.mean(y, axis=1)
    return y


def compute_features(y: np.ndarray, sr: int, hop_s: float = 0.01, win_s: float = 0.025) -> FrameTrack:
    y = _to_mono(y)
    hop = max(1, int(sr * hop_s))
    win = max(hop + 1, int(sr * win_s))
    if win % 2:
        win += 1
    window = np.hanning(win)
    freqs = rfftfreq(win, 1 / sr)
    hi = freqs >= 3500
    lo = freqs < 2000
    n_frames = max(1, 1 + (len(y) - win) // hop)
    rms = np.zeros(n_frames)
    ratio = np.zeros(n_frames)
    zcr = np.zeros(n_frames)
    prev = None
    flux = np.zeros(n_frames)
    for i in range(n_frames):
        start = i * hop
        frame = y[start : start + win]
        if len(frame) < win:
            padded = np.zeros(win)
            padded[: len(frame)] = frame
            frame = padded
        windowed = frame * window
        rms[i] = np.sqrt(np.mean(windowed**2))
        spec = np.abs(rfft(windowed))
        e_hi = float(np.sum(spec[hi] ** 2))
        e_lo = float(np.sum(spec[lo] ** 2)) + 1e-12
        ratio[i] = e_hi / e_lo
        zcr[i] = float(np.mean(np.abs(np.diff(np.signbit(frame)))))
        mag = spec + 1e-12
        if prev is None:
            flux[i] = 0.0
        else:
            diff = mag - prev
            flux[i] = float(np.sum(diff[diff > 0]))
        prev = mag
    times = (np.arange(n_frames) * hop + win / 2) / sr
    return FrameTrack(sr=sr, hop=hop, times=times, rms=rms, ratio=ratio, zcr=zcr, flux=flux)


def close_short_gaps(mask: np.ndarray, max_gap: int) -> np.ndarray:
    out = mask.copy()
    n = len(out)
    i = 0
    while i < n:
        if out[i]:
            i += 1
            continue
        j = i
        while j < n and not out[j]:
            j += 1
        if i > 0 and j < n and (j - i) <= max_gap:
            out[i:j] = True
        i = j
    return out


def runs(mask: np.ndarray) -> list[tuple[int, int]]:
    out: list[tuple[int, int]] = []
    n = len(mask)
    i = 0
    while i < n:
        if not mask[i]:
            i += 1
            continue
        j = i
        while j < n and mask[j]:
            j += 1
        out.append((i, j))
        i = j
    return out
