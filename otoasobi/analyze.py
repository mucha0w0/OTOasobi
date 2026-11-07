from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import soundfile as sf

from otoasobi.breath import apply_leading_breath
from otoasobi.features import compute_features, loudness_db
from otoasobi.oto import (
    OTO_INI_NAME,
    OtoSettings,
    _names_for_file,
    merge_existing_aliases,
    number_duplicate_aliases,
    read_oto_ini_lines,
    _strip_alias_marks,
    apply_alias_names,
    apply_file_adjustments,
    cvvc_oto_lines,
    mono_oto_lines,
    renzoku_oto_lines,
    write_oto_ini,
)
from otoasobi.parser import Token, parse_stem, stem_from_path
from otoasobi.phones import VOWEL_ONSET_LABELS
from otoasobi.segment import Span, segment_tokens
from otoasobi.wavs import AnalyzedFile, list_wavs


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


def analyze_wav(
    path: str | Path,
    on_step: Callable[[float, str], None] | None = None,
) -> Analysis:
    def step(fraction: float, label: str) -> None:
        if on_step is not None:
            on_step(fraction, label)

    step(0.0, "読み込んでいます")
    path = Path(path)
    stem = stem_from_path(path.name)
    tokens = parse_stem(stem)
    y, sr = load_wav(path)
    duration = len(y) / sr

    def on_frames(done: int, total: int) -> None:
        if total > 0:
            step(0.04 + 0.24 * done / total, "特徴を計算しています")

    step(0.04, "特徴を計算しています")
    track = compute_features(y, sr, on_progress=on_frames)
    engine = "acoustic"
    spans = None
    try:
        from otoasobi.precision import segment_precision

        spans, engine = segment_precision(tokens, y, sr, track, duration, on_step)
    except Exception:
        import traceback

        traceback.print_exc()
        spans = None
        engine = "acoustic"
    if spans is None:
        step(0.40, "音の位置を分けています")
        spans = segment_tokens(tokens, track, duration)
        engine = "acoustic"
    try:
        step(0.94, "息を分けています")
        spans = apply_leading_breath(spans, y, sr, track)
    except Exception:
        import traceback

        traceback.print_exc()
    step(1.0, "息を分けています")
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


def spans_to_txt(spans: list[Span]) -> str:
    """Full span log: start, end, phoneme, kana."""
    lines = [
        f"{span.start:.6f}\t{span.end:.6f}\t{span.label}\t{span.token}"
        for span in spans
    ]
    return "\n".join(lines) + ("\n" if lines else "")


def vowel_onset_rows(spans: list[Span]) -> list[tuple[str, float]]:
    """Kana label and the start time of each vowel, N, or M."""
    rows: list[tuple[str, float]] = []
    for span in spans:
        if span.label not in VOWEL_ONSET_LABELS:
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


def skipped_notice(skipped: list[str]) -> str:
    names = "\n".join(skipped)
    return f"{len(skipped)}個の解析できないファイルをスキップしました。スキップしたファイル↓\n{names}"


def analyze_folder_files(
    folder: str | Path,
    on_progress: Callable[[int, int], None] | None = None,
    on_file: Callable[[str, float, str], None] | None = None,
) -> tuple[list[AnalyzedFile], list[str]]:
    """フォルダ内の WAV を解析する。ファイルはまだ書かない。"""
    folder = Path(folder)
    if not folder.is_dir():
        raise RuntimeError("フォルダが見つかりません。")
    wavs = list_wavs(folder)
    if not wavs:
        raise RuntimeError("フォルダ内にWAVファイルがありません。")
    total = len(wavs)
    if on_progress is not None:
        on_progress(0, total)
    files: list[AnalyzedFile] = []
    skipped: list[str] = []
    for index, wav in enumerate(wavs, start=1):
        def on_step(fraction: float, label: str, name: str = wav.name) -> None:
            if on_file is not None:
                on_file(name, fraction, label)

        try:
            analysis = analyze_wav(wav, on_step if on_file is not None else None)
        except Exception:
            skipped.append(wav.name)
        else:
            files.append(
                AnalyzedFile(
                    name=wav.name,
                    duration=analysis.duration,
                    spans=analysis.spans,
                    engine=analysis.engine,
                )
            )
        if on_progress is not None:
            on_progress(index, total)
    return files, skipped


def _file_loudness(folder: Path, name: str) -> np.ndarray | None:
    """書き出し時のブランク用。読めないファイルは母音終了へ戻す。"""
    path = folder / name
    if not path.is_file():
        return None
    try:
        y, sr = load_wav(path)
    except Exception:
        return None
    if sr <= 0 or len(y) == 0:
        return None
    return loudness_db(y, sr)


def export_folder(
    folder: str | Path,
    files: list[AnalyzedFile],
    settings: OtoSettings | None = None,
    adjustments: list[dict] | None = None,
    parts: list[tuple[OtoSettings | None, list[dict] | None]] | None = None,
    scope: str = "all",
) -> Path:
    """直した区間から oto.ini とラベルを書く。settings が無いときはいまの初期値。

    parts があるときは、その順のエイリアスを 1 つの oto.ini に足す。
    """
    folder = Path(folder)
    if not folder.is_dir():
        raise RuntimeError("フォルダが見つかりません。")
    if not files:
        raise RuntimeError("書き出す解析結果がありません。")
    span_entries = [(item.name, item.spans) for item in files]
    oto_lines: list[str] = []
    pinned: set[int] = set()
    # parts は選んだエイリアス順。ファイルごとに、その順で行を足す。
    if parts is None:
        parts = [(settings, adjustments)]
    for item in files:
        loudness = _file_loudness(folder, item.name)
        for part_settings, part_adjustments in parts:
            if part_settings is not None and part_settings.mode == "renzoku":
                write_lines = renzoku_oto_lines
            elif part_settings is not None and part_settings.mode == "cvvc":
                write_lines = cvvc_oto_lines
            else:
                write_lines = mono_oto_lines
            part_adjustments = part_adjustments or []
            names = _names_for_file(part_adjustments, item.name)
            lines = apply_alias_names(
                apply_file_adjustments(
                    write_lines(
                        item.name,
                        item.spans,
                        item.duration,
                        part_settings,
                        loudness,
                    ),
                    item.name,
                    part_adjustments,
                    item.duration,
                ),
                names,
            )
            start = len(oto_lines)
            for index in names:
                if 0 <= index < len(lines):
                    pinned.add(start + index)
            oto_lines.extend(lines)
    out = folder / OTO_INI_NAME
    log = folder / BATCH_LOG_NAME
    onset = folder / BATCH_TXT_NAME
    log.write_text(format_batch_log(span_entries), encoding="utf-8")
    onset.write_text(format_batch_txt(span_entries), encoding="utf-8")
    if scope not in ("missing", "overlap"):
        scope = "all"
    if scope != "all" and out.is_file():
        numbered = number_duplicate_aliases(_strip_alias_marks(oto_lines), pinned)
        oto_lines = merge_existing_aliases(read_oto_ini_lines(out), numbered, scope)
        write_oto_ini(out, oto_lines, already_numbered=True)
    else:
        write_oto_ini(out, oto_lines, pinned)
    return out
