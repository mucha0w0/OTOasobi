"""WAV をモノラルの float に読む。プレビューは解析パッケージを経由しない。"""

from __future__ import annotations

from pathlib import Path

import numpy as np
import soundfile as sf


def load_wav(path: str | Path) -> tuple[np.ndarray, int]:
    y, sr = sf.read(str(path), always_2d=False)
    y = np.asarray(y, dtype=np.float64)
    if y.ndim > 1:
        y = np.mean(y, axis=1)
    peak = np.max(np.abs(y))
    if peak > 0:
        y = y / peak * 0.99
    return y, int(sr)
