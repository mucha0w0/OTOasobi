"""単独音モードの oto.ini。

1 行は次の形です。値はミリ秒、小数第 3 位です。

    ファイル名.wav=エイリアス,オフセット,子音部,ブランク,先行発声,オーバーラップ

オフセットはファイル先頭から。子音部・先行発声・オーバーラップはオフセットから。
ブランクは負の値で書き、オフセットからの時間に -1 を掛けたものです。
ブランクの位置は音量の上辺の右端から、秒数で前後にずらします。
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np

from otoasobi.blank import release_blank_seconds
from otoasobi.phones import BREATH_LABEL, SILENCE_LABEL, VOWEL_ONSET_LABELS
from otoasobi.segment import Span

# 子音開始の前、母音開始の後に置く余白。画面の初期値。
PAD_MS = 200.0
# 子音の開始と終了（母音の開始）のあいだの位置。種類ごとの比率は後で足す。
OVERLAP_AT = 0.5
# ブランクと子音部のあいだがこれより短いと、UTAU のエンジンが落ちる。
MIN_GAP_MS = 1.0
# 画面のスライダーより外の値は、ここに収める。
MAX_PAD_MS = 2000.0
# 上辺の右端からずらす幅。画面のブランクスライダーと同じ。
MAX_BLANK_SHIFT_S = 1.0
OTO_INI_NAME = "oto.ini"
# 日本語 Windows の ANSI。
ANSI = "cp932"


@dataclass(frozen=True)
class OtoSettings:
    """oto.ini の位置。未指定のときはいまの書き出しと同じ。"""

    offset_ms: float = PAD_MS
    consonant_ms: float = PAD_MS
    # 上辺の右端からの秒。正は減衰側、負は手前。
    blank_at: float = 0.0
    overlap_at: float = OVERLAP_AT


def _bounded(value: object, default: float, lo: float, hi: float) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return default
    number = float(value)
    if number != number:
        return default
    return max(lo, min(hi, number))


def oto_settings_from(raw: object) -> OtoSettings:
    """画面からの config。無い欄と範囲外は初期値か、許す範囲の端に寄せる。"""
    if not isinstance(raw, dict):
        return OtoSettings()
    return OtoSettings(
        offset_ms=_bounded(raw.get("offset_ms"), PAD_MS, 0.0, MAX_PAD_MS),
        consonant_ms=_bounded(raw.get("consonant_ms"), PAD_MS, 0.0, MAX_PAD_MS),
        blank_at=_bounded(raw.get("blank"), 0.0, -MAX_BLANK_SHIFT_S, MAX_BLANK_SHIFT_S),
        overlap_at=_bounded(raw.get("overlap"), OVERLAP_AT, 0.0, 1.0),
    )


def _ms(seconds: float) -> float:
    return round(float(seconds) * 1000.0, 3)


def _fmt(value: float) -> str:
    rounded = round(float(value), 3)
    if abs(rounded) < 0.0005:
        rounded = 0.0
    return f"{rounded:.3f}"


def _consonant_start(spans: list[Span], index: int, vowel: Span) -> float:
    """子音区間の開始。母音だけの音と、ん は母音開始を子音開始とみなす。"""
    if index <= 0:
        return vowel.start
    prev = spans[index - 1]
    if prev.token != vowel.token or not prev.token:
        return vowel.start
    if prev.label in VOWEL_ONSET_LABELS:
        return vowel.start
    if prev.label in (SILENCE_LABEL, BREATH_LABEL):
        return vowel.start
    return prev.start


def _place_blank(blank_at: float, offset: float, consonant: float, duration_ms: float) -> tuple[float, float]:
    """ブランク位置を、子音部の終わりから 1ms 以上空け、ファイル内に収める。"""
    fixed_end = round(offset + consonant, 3)
    end = blank_at
    if end < round(fixed_end + MIN_GAP_MS, 3):
        end = round(fixed_end + MIN_GAP_MS, 3)
    if end > duration_ms:
        end = duration_ms
    if end < round(fixed_end + MIN_GAP_MS, 3):
        consonant = max(0.0, round(end - MIN_GAP_MS - offset, 3))
        fixed_end = round(offset + consonant, 3)
        if end < round(fixed_end + MIN_GAP_MS, 3):
            end = round(fixed_end + MIN_GAP_MS, 3)
    blank = round(-1.0 * (end - offset), 3)
    gap = round((-blank) - consonant, 3)
    if gap < MIN_GAP_MS:
        blank = round(-(consonant + MIN_GAP_MS), 3)
    return blank, consonant


def mono_oto_line(
    filename: str,
    spans: list[Span],
    index: int,
    duration: float,
    settings: OtoSettings | None = None,
    loudness: np.ndarray | None = None,
) -> str | None:
    """1 エイリアス分の単独音 oto.ini 行。母音でも ん でもなければ None。"""
    settings = settings or OtoSettings()
    vowel = spans[index]
    if vowel.label not in VOWEL_ONSET_LABELS:
        return None
    alias = vowel.token or Path(filename).stem
    c0 = _ms(_consonant_start(spans, index, vowel))
    v0 = _ms(vowel.start)
    v1 = _ms(vowel.end)
    dur = max(_ms(duration), v1, c0)

    offset = max(0.0, round(c0 - settings.offset_ms, 3))
    preutter = max(0.0, round(v0 - offset, 3))
    overlap = round((c0 + (v0 - c0) * settings.overlap_at) - offset, 3)
    consonant = max(0.0, round((v0 + settings.consonant_ms) - offset, 3))
    blank_at = _ms(
        release_blank_seconds(loudness, duration, spans, index, settings.blank_at)
    )
    blank, consonant = _place_blank(blank_at, offset, consonant, dur)
    return (
        f"{filename}={alias},{_fmt(offset)},{_fmt(consonant)},{_fmt(blank)},{_fmt(preutter)},{_fmt(overlap)}"
    )


def mono_oto_lines(
    filename: str,
    spans: list[Span],
    duration: float,
    settings: OtoSettings | None = None,
    loudness: np.ndarray | None = None,
) -> list[str]:
    lines: list[str] = []
    for index in range(len(spans)):
        line = mono_oto_line(filename, spans, index, duration, settings, loudness)
        if line is not None:
            lines.append(line)
    return lines


def render_oto_ini(lines: list[str]) -> str:
    if not lines:
        return ""
    return "\n".join(lines) + "\n"


def _rename_existing_as_log(path: Path) -> None:
    """同名ファイルがあるときはログ名へ退避する。oto.ini は oto.log.ini、重複時は oto.log.2.ini。"""
    if not path.is_file():
        return
    stem = path.stem
    suffix = path.suffix
    index = 1
    while True:
        name = f"{stem}.log{suffix}" if index == 1 else f"{stem}.log.{index}{suffix}"
        candidate = path.with_name(name)
        if not candidate.exists():
            path.rename(candidate)
            return
        index += 1


def write_oto_ini(path: str | Path, lines: list[str]) -> Path:
    path = Path(path)
    _rename_existing_as_log(path)
    path.write_text(render_oto_ini(lines), encoding=ANSI, newline="\r\n")
    return path
