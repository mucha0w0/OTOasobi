"""波形の包絡、声の大きさ、メルスペクトログラム。補正画面がこれを描く。

表示範囲とピクセル数を受け取り、その画面にちょうど合う解像度で描く。
波形は 1 列が 1 ピクセルの最小最大で、サンプル数が列数以下なら全サンプルを返す。
旧画面のスペクトラムは、同じ範囲を指定ピクセルへ最近傍で描いた PNG です。
配布画面は、ファイルを開いたときにメルを 1 回計算し、表示範囲をそこから切り出します。
色は付けず、低い帯を下にした色番号の列を返します。横は 1024 列までです。
"""

from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass

import numpy as np

from otoasobi.features import (
    _display_fft,
    _display_window,
    _mel_filterbank,
    _preemphasize,
    loudness_db,
    spectrogram_tone,
)

INDEX_COLUMNS = 1024
_FILM_CHUNK = 2048

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
    loudness: list[float]
    png: bytes
    spec_t0: float
    spec_t1: float
    wave_t0: float
    wave_t1: float
    samples: bool
    sr: int


_FB: dict[tuple[int, int, int], np.ndarray] = {}


def _nums(values: np.ndarray) -> list[float]:
    return [round(float(value), 5) for value in values]


def _bounds(n: int, sr: int, t0: float, t1: float) -> tuple[float, float, int, int]:
    duration = n / float(sr) if sr > 0 else 0.0
    view0 = float(np.clip(t0, 0.0, duration))
    view1 = float(np.clip(t1, view0, duration))
    if view1 <= view0 and sr > 0 and n > 0:
        view1 = min(duration, view0 + 1.0 / sr)
    i0 = int(np.clip(np.floor(view0 * sr + 1e-9), 0, n)) if sr > 0 else 0
    i1 = int(np.clip(np.ceil(view1 * sr - 1e-9), 0, n)) if sr > 0 else 0
    if n > 0 and i1 <= i0:
        i1 = min(n, i0 + 1)
    return view0, view1, i0, i1


def waveform_view(
    y: np.ndarray, sr: int, t0: float, t1: float, columns: int
) -> tuple[np.ndarray, np.ndarray, float, float, bool]:
    """表示範囲の波形。列よりサンプルが少なければ 1 点が 1 サンプル。"""
    y = np.asarray(y, dtype=np.float64).reshape(-1)
    n = int(y.shape[0])
    if sr <= 0 or n == 0:
        return np.zeros(1), np.zeros(1), 0.0, 1.0, False
    _view0, _view1, i0, i1 = _bounds(n, sr, t0, t1)
    segment = y[i0:i1]
    count = int(segment.shape[0])
    wt0 = i0 / float(sr)
    wt1 = i1 / float(sr)
    columns = max(1, int(columns))
    if count <= columns:
        values = np.array(segment, dtype=np.float64, copy=True)
        return values, values.copy(), wt0, wt1, True
    edges = np.linspace(0, count, columns + 1)
    edges = np.floor(edges).astype(np.int64)
    edges[0] = 0
    edges[-1] = count
    np.maximum.accumulate(edges, out=edges)
    starts = np.minimum(edges[:-1], count - 1)
    lo = np.minimum.reduceat(segment, starts)
    hi = np.maximum.reduceat(segment, starts)
    return lo, hi, wt0, wt1, False


def _filterbank(sr: int, n_fft: int, n_mels: int) -> np.ndarray:
    key = (sr, n_fft, n_mels)
    cached = _FB.get(key)
    if cached is not None:
        return cached
    fmax = min(8000.0, sr / 2.0)
    fmin = min(40.0, fmax * 0.25)
    bank, _centers = _mel_filterbank(sr, n_fft, n_mels, fmin, fmax)
    _FB[key] = bank
    return bank


def _mel_db(y: np.ndarray, sr: int, i0: int, i1: int, max_frames: int, n_mels: int = 96) -> np.ndarray:
    """[i0, i1) に中心があるフレーム。窓は範囲の外のサンプルも使う。"""
    y = _preemphasize(np.asarray(y, dtype=np.float64).reshape(-1))
    n = int(y.shape[0])
    win = _display_window(sr)
    n_fft = _display_fft(win)
    n_mels = min(n_mels, max(16, n_fft // 2))
    if n == 0 or i1 <= i0:
        return np.zeros((n_mels, 1))
    span = max(1, i1 - i0)
    max_frames = max(1, int(max_frames))
    hop = max(1, int(np.ceil(span / max_frames)))
    centers = np.arange(i0, i1, hop, dtype=np.int64)
    if len(centers) == 0:
        centers = np.array([i0], dtype=np.int64)
    half = win // 2
    padded = np.pad(y, (half, half))
    n_frames = int(len(centers))
    hop = int(centers[1] - centers[0]) if n_frames > 1 else 1
    base = int(centers[0])
    needed = (n_frames - 1) * hop + win
    view = np.lib.stride_tricks.as_strided(
        padded[base : base + needed],
        shape=(n_frames, win),
        strides=(hop * padded.strides[0], padded.strides[0]),
        writeable=False,
    )
    power = np.abs(np.fft.rfft(view * np.hanning(win), n=n_fft, axis=1)) ** 2
    db = 10.0 * np.log10(np.maximum(power @ _filterbank(sr, n_fft, n_mels).T, 1e-10))
    peak = float(np.max(db))
    db = np.clip(db, peak - 80.0, peak)
    return np.ascontiguousarray(db.T)


def _png_rgb(rgb: np.ndarray) -> bytes:
    image = np.ascontiguousarray(rgb, dtype=np.uint8)
    height, width, _channels = image.shape
    raw = b"".join(b"\x00" + image[row].tobytes() for row in range(height))

    def chunk(tag: bytes, data: bytes) -> bytes:
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 3)) + chunk(b"IEND", b"")


def spectrogram_png(
    y: np.ndarray,
    sr: int,
    t0: float,
    t1: float,
    width: int,
    height: int,
) -> tuple[bytes, float, float]:
    """表示範囲を width×height へ最近傍で描く。低域が下。"""
    y = np.asarray(y, dtype=np.float64).reshape(-1)
    n = int(y.shape[0])
    width = max(1, int(width))
    height = max(1, int(height))
    if sr <= 0 or n == 0:
        return _png_rgb(np.zeros((height, width, 3), dtype=np.uint8)), 0.0, 1.0
    view0, view1, i0, i1 = _bounds(n, sr, t0, t1)
    db = _mel_db(y, sr, i0, i1, width)
    n_mels, n_frames = int(db.shape[0]), int(db.shape[1])
    color_index = np.clip(np.rint(spectrogram_tone(db) * 255.0), 0, 255).astype(np.int32)
    cols = np.minimum((np.arange(width) * n_frames) // width, n_frames - 1)
    rows = (np.arange(height) * n_mels) // height
    rows = n_mels - 1 - np.minimum(rows, n_mels - 1)
    rgb = np.ascontiguousarray(_LUT[color_index[rows][:, cols]])
    return _png_rgb(rgb), view0, view1


def render_preview(
    y: np.ndarray,
    sr: int,
    t0: float | None = None,
    t1: float | None = None,
    columns: int = 1600,
    rows: int = 320,
) -> Preview:
    y = np.asarray(y, dtype=np.float64).reshape(-1)
    n = int(y.shape[0])
    duration = n / float(sr) if sr > 0 and n else 0.0
    if t0 is None:
        t0 = 0.0
    if t1 is None:
        t1 = duration
    lo, hi, wt0, wt1, samples = waveform_view(y, sr, t0, t1, columns)
    png, spec_t0, spec_t1 = spectrogram_png(y, sr, t0, t1, columns, rows)
    full = duration <= 0 or (t1 - t0) >= duration * 0.999
    loud = loudness_db(y, sr) if full else np.zeros(0)
    return Preview(
        wave_lo=_nums(lo),
        wave_hi=_nums(hi),
        loudness=_nums(loud),
        png=png,
        spec_t0=spec_t0,
        spec_t1=spec_t1,
        wave_t0=wt0,
        wave_t1=wt1,
        samples=samples,
        sr=int(sr),
    )


@dataclass(frozen=True)
class Film:
    db: np.ndarray
    hop: int
    sr: int
    n: int


_film: tuple[np.ndarray, int, Film] | None = None


def _wave(y: np.ndarray) -> np.ndarray:
    if y.dtype == np.float64 and y.ndim == 1 and y.flags.c_contiguous:
        return y
    return np.ascontiguousarray(np.asarray(y, dtype=np.float64).reshape(-1))


def film_ready(y: np.ndarray, sr: int) -> bool:
    cached = _film
    return cached is not None and cached[0] is y and cached[1] == sr


def film_for(y: np.ndarray, sr: int) -> Film:
    """渡された配列のメルを、最初の 1 回だけ計算する。"""
    global _film
    cached = _film
    if cached is not None and cached[0] is y and cached[1] == sr:
        return cached[2]
    film = _build_film(_wave(y), sr)
    _film = (y, sr, film)
    return film


def _build_film(y: np.ndarray, sr: int) -> Film:
    n = int(y.shape[0])
    win = _display_window(sr)
    if 0 < n < win:
        win = max(64, 1 << int(np.floor(np.log2(max(n, 64)))))
    n_fft = _display_fft(win)
    hop = max(1, win // 4)
    n_mels = min(96, max(16, n_fft // 2))
    if sr <= 0 or n == 0:
        return Film(np.zeros((n_mels, 1), dtype=np.float32), hop, sr, n)
    emphasized = _preemphasize(y)
    fmax = min(8000.0, sr / 2.0)
    fmin = min(40.0, fmax * 0.25)
    bank = _filterbank(sr, n_fft, n_mels).T
    pad = win // 2
    padded = np.pad(emphasized, (pad, pad))
    n_frames = 1 + (len(padded) - win) // hop
    frames = np.lib.stride_tricks.as_strided(
        padded,
        shape=(n_frames, win),
        strides=(hop * padded.strides[0], padded.strides[0]),
        writeable=False,
    )
    window = np.hanning(win)
    db = np.empty((n_mels, n_frames), dtype=np.float32)
    for start in range(0, n_frames, _FILM_CHUNK):
        end = min(n_frames, start + _FILM_CHUNK)
        power = np.abs(np.fft.rfft(frames[start:end] * window, n=n_fft, axis=1)) ** 2
        db[:, start:end] = (10.0 * np.log10(np.maximum(power @ bank, 1e-10))).T
    return Film(db, hop, sr, n)


def _frame_slice(film: Film, i0: int, i1: int) -> np.ndarray:
    hop = film.hop
    n_frames = int(film.db.shape[1])
    if n_frames <= 1 or hop <= 0:
        return film.db
    k0 = int(np.ceil(i0 / float(hop) - 1e-9))
    k1 = int(np.floor(i1 / float(hop) + 1e-9)) + 1
    k0 = min(max(0, k0), n_frames - 1)
    k1 = min(max(k0 + 1, k1), n_frames)
    return film.db[:, k0:k1]


def spectrogram_index(
    y: np.ndarray,
    sr: int,
    t0: float,
    t1: float,
    columns: int,
) -> tuple[bytes, int, int, float, float]:
    """表示範囲の色番号。行は低い帯が先、列は時間。"""
    columns = max(1, min(int(columns), INDEX_COLUMNS))
    film = film_for(y, sr)
    n = film.n
    n_mels = int(film.db.shape[0])
    if sr <= 0 or n == 0:
        return bytes(n_mels * columns), columns, n_mels, 0.0, 1.0
    view0, view1, i0, i1 = _bounds(n, sr, t0, t1)
    frames = _frame_slice(film, i0, i1)
    n_frames = int(frames.shape[1])
    if n_frames > columns:
        pick = np.minimum((np.arange(columns) * n_frames) // columns, n_frames - 1)
        frames = frames[:, pick]
        n_frames = columns
    peak = float(np.max(frames))
    clipped = np.clip(frames, peak - 80.0, peak)
    tone = np.clip(np.rint(spectrogram_tone(clipped) * 255.0), 0, 255).astype(np.uint8)
    indices = np.ascontiguousarray(tone[::-1])
    return indices.tobytes(), n_frames, n_mels, view0, view1


@dataclass(frozen=True)
class IndexPreview:
    wave_lo: list[float]
    wave_hi: list[float]
    loudness: list[float]
    spec: bytes
    spec_columns: int
    spec_rows: int
    spec_t0: float
    spec_t1: float
    wave_t0: float
    wave_t1: float
    samples: bool
    sr: int
    duration: float


def index_wave(
    y: np.ndarray,
    sr: int,
    t0: float,
    t1: float,
    columns: int,
) -> IndexPreview:
    """メルを計算せず、波形と、全体を見ているときの音量だけ返す。"""
    n = int(np.asarray(y).reshape(-1).shape[0])
    duration = n / float(sr) if sr > 0 and n else 0.0
    columns = max(1, min(int(columns), INDEX_COLUMNS))
    lo, hi, wt0, wt1, samples = waveform_view(y, sr, t0, t1, columns)
    full = duration <= 0 or (t1 - t0) >= duration * 0.999
    loud = loudness_db(y, sr) if full else np.zeros(0)
    return IndexPreview(
        wave_lo=_nums(lo),
        wave_hi=_nums(hi),
        loudness=_nums(loud),
        spec=b"",
        spec_columns=0,
        spec_rows=0,
        spec_t0=0.0,
        spec_t1=0.0,
        wave_t0=wt0,
        wave_t1=wt1,
        samples=samples,
        sr=int(sr),
        duration=duration,
    )


def render_index(
    y: np.ndarray,
    sr: int,
    t0: float | None = None,
    t1: float | None = None,
    columns: int = 1024,
) -> IndexPreview:
    n = int(np.asarray(y).reshape(-1).shape[0])
    duration = n / float(sr) if sr > 0 and n else 0.0
    if t0 is None:
        t0 = 0.0
    if t1 is None or t1 <= t0:
        t1 = duration
    columns = max(1, min(int(columns), INDEX_COLUMNS))
    lo, hi, wt0, wt1, samples = waveform_view(y, sr, t0, t1, columns)
    spec, spec_columns, spec_rows, spec_t0, spec_t1 = spectrogram_index(y, sr, t0, t1, columns)
    full = duration <= 0 or (t1 - t0) >= duration * 0.999
    loud = loudness_db(y, sr) if full else np.zeros(0)
    return IndexPreview(
        wave_lo=_nums(lo),
        wave_hi=_nums(hi),
        loudness=_nums(loud),
        spec=spec,
        spec_columns=spec_columns,
        spec_rows=spec_rows,
        spec_t0=spec_t0,
        spec_t1=spec_t1,
        wave_t0=wt0,
        wave_t1=wt1,
        samples=samples,
        sr=int(sr),
        duration=duration,
    )
