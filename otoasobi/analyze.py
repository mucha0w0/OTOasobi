from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np
import soundfile as sf

from otoasobi.features import compute_features
from otoasobi.parser import Token, parse_stem, stem_from_path
from otoasobi.segment import Span, segment_tokens


@dataclass
class Analysis:
    path: Path
    stem: str
    sr: int
    y: np.ndarray
    duration: float
    tokens: list[Token]
    spans: list[Span]


def load_wav(path: str | Path) -> tuple[np.ndarray, int]:
    y, sr = sf.read(str(path), always_2d=False)
    y = np.asarray(y, dtype=np.float64)
    if y.ndim > 1:
        y = np.mean(y, axis=1)
    peak = np.max(np.abs(y))
    if peak > 0:
        y = y / peak * 0.99
    return y, int(sr)


def analyze_wav(path: str | Path) -> Analysis:
    path = Path(path)
    stem = stem_from_path(path.name)
    tokens = parse_stem(stem)
    y, sr = load_wav(path)
    duration = len(y) / sr
    track = compute_features(y, sr)
    spans = segment_tokens(tokens, track, duration)
    return Analysis(
        path=path,
        stem=stem,
        sr=sr,
        y=y,
        duration=duration,
        tokens=tokens,
        spans=spans,
    )


def spans_to_txt(spans: list[Span], *, include_token: bool = True) -> str:
    lines = []
    for span in spans:
        row = f"{span.start:.6f}\t{span.end:.6f}\t{span.label}"
        if include_token:
            row += f"\t{span.token}"
        lines.append(row)
    return "\n".join(lines) + "\n"


def write_label_txt(path: str | Path, spans: list[Span], *, include_token: bool = True) -> Path:
    path = Path(path)
    path.write_text(spans_to_txt(spans, include_token=include_token), encoding="utf-8")
    return path


def default_txt_path(wav_path: str | Path) -> Path:
    wav_path = Path(wav_path)
    return wav_path.with_suffix(".txt")
