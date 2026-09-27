from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np
import soundfile as sf

from otoasobi.breath import apply_leading_breath
from otoasobi.features import compute_features
from otoasobi.parser import FilenameError, Token, parse_stem, stem_from_path
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
    engine: str = "acoustic"


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
    engine = "acoustic"
    spans = None
    try:
        from otoasobi.precision import segment_precision

        spans, engine = segment_precision(tokens, y, sr, track, duration)
    except Exception:
        import traceback

        traceback.print_exc()
        spans = None
        engine = "acoustic"
    if spans is None:
        spans = segment_tokens(tokens, track, duration)
        engine = "acoustic"
    try:
        spans = apply_leading_breath(spans, y, sr, track)
    except Exception:
        import traceback

        traceback.print_exc()
    return Analysis(
        path=path,
        stem=stem,
        sr=sr,
        y=y,
        duration=duration,
        tokens=tokens,
        spans=spans,
        engine=engine,
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


BATCH_TXT_NAME = "OTOasibi_out.txt"


def list_wavs(folder: str | Path) -> list[Path]:
    folder = Path(folder)
    wavs = [path for path in folder.iterdir() if path.is_file() and path.suffix.lower() == ".wav"]
    return sorted(wavs, key=lambda path: path.name.casefold())


def format_batch_txt(entries: list[tuple[str, list[Span]]]) -> str:
    blocks = []
    for name, spans in entries:
        body = spans_to_txt(spans).rstrip("\n")
        blocks.append(f"[{name}]\n{body}")
    return "\n\n".join(blocks) + "\n"


def analyze_folder(folder: str | Path) -> Path:
    folder = Path(folder)
    if not folder.is_dir():
        raise RuntimeError("フォルダが見つかりません。")
    wavs = list_wavs(folder)
    if not wavs:
        raise RuntimeError("フォルダ内にWAVファイルがありません。")
    entries: list[tuple[str, list[Span]]] = []
    for wav in wavs:
        try:
            analysis = analyze_wav(wav)
        except FilenameError as exc:
            raise FilenameError(f"{wav.name}: {exc}") from exc
        except Exception as exc:
            raise RuntimeError(f"{wav.name}: {exc}") from exc
        entries.append((wav.name, analysis.spans))
    out = folder / BATCH_TXT_NAME
    out.write_text(format_batch_txt(entries), encoding="utf-8")
    return out
