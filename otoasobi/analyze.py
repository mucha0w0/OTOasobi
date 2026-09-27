from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import soundfile as sf

from otoasobi.breath import apply_leading_breath
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


# a i u e o, plus ん (N / M). っ (cl), 無音, and 息 are not onsets.
_VOWEL_ONSET = frozenset({"a", "i", "u", "e", "o", "N", "M"})


def spans_to_txt(spans: list[Span], *, include_token: bool = True) -> str:
    """Full span log: start, end, phoneme, kana."""
    lines = []
    for span in spans:
        row = f"{span.start:.6f}\t{span.end:.6f}\t{span.label}"
        if include_token:
            row += f"\t{span.token}"
        lines.append(row)
    return "\n".join(lines) + ("\n" if lines else "")


def vowel_onset_rows(spans: list[Span]) -> list[tuple[str, float]]:
    """Kana label and the start time of each vowel, N, or M."""
    rows: list[tuple[str, float]] = []
    for span in spans:
        if span.label not in _VOWEL_ONSET:
            continue
        rows.append((span.token or span.label, span.start))
    return rows


def onsets_to_txt(spans: list[Span]) -> str:
    """User-facing lines: kana, then the vowel-onset time in seconds."""
    lines = [f"{label}\t{start:.6f}" for label, start in vowel_onset_rows(spans)]
    return "\n".join(lines) + ("\n" if lines else "")


def write_label_txt(path: str | Path, spans: list[Span]) -> Path:
    path = Path(path)
    path.write_text(onsets_to_txt(spans), encoding="utf-8")
    return path


def write_log_txt(path: str | Path, spans: list[Span]) -> Path:
    path = Path(path)
    path.write_text(spans_to_txt(spans), encoding="utf-8")
    return path


def default_txt_path(wav_path: str | Path) -> Path:
    wav_path = Path(wav_path)
    return wav_path.with_suffix(".txt")


def default_log_path(wav_path: str | Path) -> Path:
    wav_path = Path(wav_path)
    return wav_path.with_suffix(".log.txt")


BATCH_TXT_NAME = "OTOasibi_out.txt"
BATCH_LOG_NAME = "OTOasibi_log.txt"


def list_wavs(folder: str | Path) -> list[Path]:
    folder = Path(folder)
    wavs = [path for path in folder.iterdir() if path.is_file() and path.suffix.lower() == ".wav"]
    return sorted(wavs, key=lambda path: path.name.casefold())


def _format_batch(entries: list[tuple[str, list[Span]]], to_text: Callable[[list[Span]], str]) -> str:
    blocks = []
    for name, spans in entries:
        body = to_text(spans).rstrip("\n")
        blocks.append(f"[{name}]\n{body}" if body else f"[{name}]")
    return "\n\n".join(blocks) + "\n"


def format_batch_txt(entries: list[tuple[str, list[Span]]]) -> str:
    return _format_batch(entries, onsets_to_txt)


def format_batch_log(entries: list[tuple[str, list[Span]]]) -> str:
    return _format_batch(entries, spans_to_txt)


@dataclass
class FolderResult:
    txt: Path | None
    skipped: list[str]


def skipped_notice(skipped: list[str]) -> str:
    names = "\n".join(skipped)
    return f"{len(skipped)}個の解析できないファイルをスキップしました。スキップしたファイル↓\n{names}"


def analyze_folder(
    folder: str | Path,
    on_progress: Callable[[int, int], None] | None = None,
) -> FolderResult:
    folder = Path(folder)
    if not folder.is_dir():
        raise RuntimeError("フォルダが見つかりません。")
    wavs = list_wavs(folder)
    if not wavs:
        raise RuntimeError("フォルダ内にWAVファイルがありません。")
    total = len(wavs)
    if on_progress is not None:
        on_progress(0, total)
    entries: list[tuple[str, list[Span]]] = []
    skipped: list[str] = []
    for index, wav in enumerate(wavs, start=1):
        try:
            analysis = analyze_wav(wav)
        except Exception:
            skipped.append(wav.name)
        else:
            entries.append((wav.name, analysis.spans))
        if on_progress is not None:
            on_progress(index, total)
    if not entries:
        return FolderResult(txt=None, skipped=skipped)
    out = folder / BATCH_TXT_NAME
    log = folder / BATCH_LOG_NAME
    out.write_text(format_batch_txt(entries), encoding="utf-8")
    log.write_text(format_batch_log(entries), encoding="utf-8")
    return FolderResult(txt=out, skipped=skipped)
