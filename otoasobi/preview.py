"""波形の包絡とメルスペクトログラム。補正画面がこれを描く。"""

from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass

import numpy as np

from otoasobi.features import mel_spectrogram

_STOPS = np.array(
    [
        [0, 0, 4],
        [28, 16, 68],
        [79, 18, 123],
        [129, 37, 129],
        [181, 54, 122],
        [229, 80, 100],
        [251, 135, 97],
        [254, 194, 135],
        [252, 253, 191],
    ],
    dtype=np.float64,
)


def _lut() -> np.ndarray:
    pos = np.linspace(0, len(_STOPS) - 1, 256)
    i0 = np.floor(pos).astype(int)
    i1 = np.minimum(i0 + 1, len(_STOPS) - 1)
    blend = (pos - i0)[:, None]
    rgb = _STOPS[i0] * (1.0 - blend) + _STOPS[i1] * blend
    return np.clip(np.rint(rgb), 0, 255).astype(np.uint8)


_LUT = _lut()


@dataclass(frozen=True)
class Preview:
    wave_lo: list[float]
    wave_hi: list[float]
    png: bytes
    spec_t0: float
    spec_t1: float


def _nums(values: np.ndarray) -> list[float]:
    return [round(float(value), 5) for value in values]


def waveform_envelope(y: np.ndarray, sr: int, bins: int = 1600) -> tuple[np.ndarray, np.ndarray]:
    """ファイル全体を等間隔に分け、各区間の最小と最大を返す。"""
    y = np.asarray(y, dtype=np.float64).reshape(-1)
    if sr <= 0 or len(y) == 0:
        return np.zeros(1), np.zeros(1)
    bins = max(1, min(bins, len(y)))
    edges = np.linspace(0, len(y), bins + 1).astype(int)
    lo = np.zeros(bins)
    hi = np.zeros(bins)
    for index in range(bins):
        start = int(edges[index])
        end = int(edges[index + 1])
        if end <= start:
            end = min(len(y), start + 1)
        chunk = y[start:end]
        if len(chunk) == 0:
            continue
        lo[index] = float(chunk.min())
        hi[index] = float(chunk.max())
    return lo, hi


def _png_rgb(rgb: np.ndarray) -> bytes:
    image = np.ascontiguousarray(rgb, dtype=np.uint8)
    height, width, _channels = image.shape
    raw = b"".join(b"\x00" + image[row].tobytes() for row in range(height))

    def chunk(tag: bytes, data: bytes) -> bytes:
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 3)) + chunk(b"IEND", b"")


def spectrogram_png(y: np.ndarray, sr: int, max_cols: int = 1400) -> tuple[bytes, float, float]:
    """低域が下になるメルスペクトログラムの PNG と、その時間範囲。"""
    mel = mel_spectrogram(y, sr)
    db = np.asarray(mel.db, dtype=np.float64)
    if db.size == 0:
        db = np.zeros((1, 1))
    cols = int(db.shape[1])
    if cols > max_cols:
        index = np.linspace(0, cols - 1, max_cols).astype(np.int64)
        db = db[:, index]
    lo = float(np.min(db))
    hi = float(np.max(db))
    norm = (db - lo) / (hi - lo + 1e-9)
    color_index = np.clip(np.rint(norm * 255.0), 0, 255).astype(np.int32)
    rgb = _LUT[color_index]
    rgb = np.ascontiguousarray(rgb[::-1])
    return _png_rgb(rgb), float(mel.t0), float(mel.t1)


def render_preview(y: np.ndarray, sr: int) -> Preview:
    lo, hi = waveform_envelope(y, sr)
    png, t0, t1 = spectrogram_png(y, sr)
    return Preview(wave_lo=_nums(lo), wave_hi=_nums(hi), png=png, spec_t0=t0, spec_t1=t1)
