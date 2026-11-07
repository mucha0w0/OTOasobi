"""フォルダ直下の WAV と、1 ファイル分の解析結果。

音の読み込みと oto.ini の書き出しは analyze が行う。
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from otoasobi.segment import Span


def list_wavs(folder: str | Path) -> list[Path]:
    folder = Path(folder)
    wavs = [path for path in folder.iterdir() if path.is_file() and path.suffix.lower() == ".wav"]
    return sorted(wavs, key=lambda path: path.name.casefold())


@dataclass
class AnalyzedFile:
    name: str
    duration: float
    spans: list[Span]
    engine: str = "acoustic"
