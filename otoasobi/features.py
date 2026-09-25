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
    centroid: np.ndarray
    bright: np.ndarray


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
    f1b = (freqs >= 250) & (freqs < 1000)
    f2b = (freqs >= 1000) & (freqs < 3000)
    n_frames = max(1, 1 + (len(y) - win) // hop)
    rms = np.zeros(n_frames)
    ratio = np.zeros(n_frames)
    zcr = np.zeros(n_frames)
    prev = None
    flux = np.zeros(n_frames)
    centroid = np.zeros(n_frames)
    bright = np.zeros(n_frames)
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
        centroid[i] = float(np.sum(freqs * mag) / np.sum(mag))
        e_f1 = float(np.sum(spec[f1b] ** 2)) + 1e-12
        e_f2 = float(np.sum(spec[f2b] ** 2))
        bright[i] = e_f2 / e_f1
        if prev is None:
            flux[i] = 0.0
        else:
            diff = mag - prev
            flux[i] = float(np.sum(diff[diff > 0]))
        prev = mag
    times = (np.arange(n_frames) * hop + win / 2) / sr
    return FrameTrack(
        sr=sr,
        hop=hop,
        times=times,
        rms=rms,
        ratio=ratio,
        zcr=zcr,
        flux=flux,
        centroid=centroid,
        bright=bright,
    )


@dataclass
class MelSpectrogram:
    db: np.ndarray
    t0: float
    t1: float
    centers_hz: np.ndarray


def _hz_to_mel(hz: np.ndarray | float) -> np.ndarray:
    return 2595.0 * np.log10(1.0 + np.asarray(hz, dtype=np.float64) / 700.0)


def _mel_to_hz(mel: np.ndarray | float) -> np.ndarray:
    return 700.0 * (10.0 ** (np.asarray(mel, dtype=np.float64) / 2595.0) - 1.0)


def _mel_filterbank(sr: int, n_fft: int, n_mels: int, fmin: float, fmax: float) -> tuple[np.ndarray, np.ndarray]:
    n_freqs = n_fft // 2 + 1
    mel_pts = np.linspace(_hz_to_mel(fmin), _hz_to_mel(fmax), n_mels + 2)
    hz_pts = _mel_to_hz(mel_pts)
    bins = np.clip(np.round(hz_pts * n_fft / sr).astype(int), 0, n_freqs - 1)
    fb = np.zeros((n_mels, n_freqs), dtype=np.float64)
    for i in range(n_mels):
        left, center, right = int(bins[i]), int(bins[i + 1]), int(bins[i + 2])
        if center <= left:
            center = min(left + 1, n_freqs - 1)
        if right <= center:
            right = min(center + 1, n_freqs - 1)
        up = np.arange(left, center)
        if len(up):
            fb[i, up] = (up - left) / (center - left)
        down = np.arange(center, right)
        if len(down):
            fb[i, down] = (right - down) / (right - center)
        fb[i, center] = 1.0
    return fb, hz_pts[1:-1]


def _stft_size(sr: int, n_samples: int) -> tuple[int, int]:
    target = max(64, int(round(0.025 * sr)))
    n_fft = 256
    while n_fft < target and n_fft < 2048:
        n_fft *= 2
    if n_samples < n_fft:
        n_fft = max(64, 1 << int(np.floor(np.log2(max(n_samples, 64)))))
    hop = max(1, n_fft // 4)
    if n_samples // hop > 3000:
        hop = max(hop, int(np.ceil(n_samples / 3000)))
    return n_fft, hop


def mel_spectrogram(y: np.ndarray, sr: int, n_mels: int = 96) -> MelSpectrogram:
    y = np.asarray(y, dtype=np.float64).reshape(-1)
    if sr <= 0 or len(y) == 0:
        centers = np.linspace(0.0, 1.0, n_mels)
        return MelSpectrogram(np.zeros((n_mels, 1)), 0.0, 1.0, centers)
    n_fft, hop = _stft_size(sr, len(y))
    n_mels = min(n_mels, max(16, n_fft // 2))
    fmax = min(8000.0, sr / 2.0)
    fmin = min(40.0, fmax * 0.25)
    pad = n_fft // 2
    padded = np.pad(y, (pad, pad))
    n_frames = 1 + (len(padded) - n_fft) // hop
    frames = np.lib.stride_tricks.as_strided(
        padded,
        shape=(n_frames, n_fft),
        strides=(padded.strides[0] * hop, padded.strides[0]),
        writeable=False,
    )
    window = np.hanning(n_fft)
    power = np.abs(np.fft.rfft(frames * window, axis=1)) ** 2
    fb, centers = _mel_filterbank(sr, n_fft, n_mels, fmin, fmax)
    db = 10.0 * np.log10(np.maximum(power @ fb.T, 1e-10))
    peak = float(np.max(db))
    db = np.clip(db, peak - 80.0, peak)
    half = hop / (2.0 * sr)
    t0 = -half
    t1 = (n_frames - 1) * hop / sr + half
    return MelSpectrogram(db=db.T, t0=t0, t1=max(t1, t0 + half), centers_hz=centers)


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
