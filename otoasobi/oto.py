"""単独音と連続音の oto.ini。

1 行は次の形です。値はミリ秒、小数第 3 位です。

    ファイル名.wav=エイリアス,オフセット,子音部,ブランク,先行発声,オーバーラップ

オフセットはファイル先頭から。子音部・先行発声・オーバーラップはオフセットから。
ブランクは負の値で書き、オフセットからの時間に -1 を掛けたものです。
子音部とブランクは、単独音も連続音も同じ位置です。
先行発声は母音の開始です。

単独音のオフセットは、子音の開始を基準に ±0.1 秒だけ前後へずらします。
単独音のオーバーラップは、0 が子音の開始、1 が母音の開始です。
負の数は、子音の開始より前の秒です。その位置がオフセットより前でも、そのまま置きます。
連続音のオフセットの 0 は、その音素の子音と直前の母音の境界から 0.15 秒手前です。
子音がない音素は、母音と母音の境界から 0.15 秒手前です。ずらす幅は単独音と同じです。
連続音のオーバーラップは、オフセットの位置を 0%、その音素の開始を 100% とした割合です。
音素の開始は、子音と直前の母音の境界です。子音がないときは、母音と母音の境界です。
アンダーバーだけの無音の直後の音素は、単独音と同じ計算です。値は rest のプリセットです。
エイリアスは連続音のままです。
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

from otoasobi.blank import release_blank_seconds
from otoasobi.phones import BREATH_LABEL, SILENCE_LABEL, VOWEL_ONSET_LABELS
from otoasobi.segment import Span

# 母音開始の後に置く子音部。画面の初期値。
PAD_MS = 200.0
# 子音の開始と終了（母音の開始）のあいだの位置。子音ラベルが無いときの比率。
OVERLAP_AT = 0.5
# ブランクと子音部のあいだがこれより短いと、UTAU のエンジンが落ちる。
MIN_GAP_MS = 1.0
# 子音部で、これより外の値はここに収める。
MAX_PAD_MS = 2000.0
# 子音の開始から前後にこの幅。画面のオフセットスライダーと同じ。正は手前。
MAX_OFFSET_SHIFT_MS = 100.0
# 連続音のオフセットの 0。音素の開始から手前へこの幅。
RENZOKU_OFFSET_LEAD_MS = 150.0
# 上辺の右端からずらす幅。画面のブランクスライダーと同じ。
MAX_BLANK_SHIFT_S = 0.1
OTO_INI_NAME = "oto.ini"
# 日本語 Windows の ANSI。
ANSI = "cp932"


@dataclass(frozen=True)
class OtoSettings:
    """oto.ini の位置。未指定のときはいまの書き出しと同じ。"""

    # ミリ秒。正は手前、負は後。0 は単独音では子音開始、連続音では音素の開始の 0.15 秒手前。
    offset_ms: float = 0.0
    consonant_ms: float = PAD_MS
    # 上辺の右端からの秒。正は減衰側、負は手前。
    blank_at: float = 0.0
    overlap_at: float = OVERLAP_AT
    # 子音ラベル（k, sh など）ごと。0〜1 は子音開始から母音開始までの比率。
    # 負の数は、子音開始より前の秒。無いラベルは overlap_at。連続音では使わない。
    overlap_by: dict[str, float] = field(default_factory=dict)
    # mono または renzoku。
    mode: str = "mono"
    # 連続音で、アンダーバーの無音の直後にだけ使う単独音の値。
    rest: OtoSettings | None = None


def _bounded(value: object, default: float, lo: float, hi: float) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return default
    number = float(value)
    if number != number:
        return default
    return max(lo, min(hi, number))


def _overlap_setting(value: object) -> float:
    """0〜1 は比率。負の数は子音開始より前の秒で、最大 0.1 秒。"""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return OVERLAP_AT
    number = float(value)
    if number != number:
        return OVERLAP_AT
    if number < 0.0:
        return max(-MAX_OFFSET_SHIFT_MS / 1000.0, number)
    return max(0.0, min(1.0, number))


def _overlap_by(raw: object) -> dict[str, float]:
    """子音ラベルごとのオーバーラップ。数値が 1 個だけの古い config は空にして、全体の比率に任せる。"""
    if not isinstance(raw, dict):
        return {}
    table: dict[str, float] = {}
    for key, value in raw.items():
        if isinstance(key, str) and key:
            table[key] = _overlap_setting(value)
    return table


def oto_settings_from(raw: object, *, with_rest: bool = True) -> OtoSettings:
    """画面からの config。無い欄と範囲外は初期値か、許す範囲の端に寄せる。"""
    if not isinstance(raw, dict):
        return OtoSettings()
    overlap_raw = raw.get("overlap")
    if isinstance(overlap_raw, dict):
        overlap_at = OVERLAP_AT
        overlap_by = _overlap_by(overlap_raw)
    else:
        overlap_at = _bounded(overlap_raw, OVERLAP_AT, 0.0, 1.0)
        overlap_by = {}
    rest = None
    if with_rest and isinstance(raw.get("rest"), dict):
        rest = oto_settings_from(raw["rest"], with_rest=False)
    return OtoSettings(
        offset_ms=_bounded(
            raw.get("offset_ms"),
            0.0,
            -MAX_OFFSET_SHIFT_MS,
            MAX_OFFSET_SHIFT_MS,
        ),
        consonant_ms=_bounded(raw.get("consonant_ms"), PAD_MS, 0.0, MAX_PAD_MS),
        blank_at=_bounded(raw.get("blank"), 0.0, -MAX_BLANK_SHIFT_S, MAX_BLANK_SHIFT_S),
        overlap_at=overlap_at,
        overlap_by=overlap_by,
        mode="renzoku" if raw.get("mode") == "renzoku" else "mono",
        rest=rest,
    )


def _ms(seconds: float) -> float:
    return round(float(seconds) * 1000.0, 3)


def _fmt(value: float) -> str:
    rounded = round(float(value), 3)
    if abs(rounded) < 0.0005:
        rounded = 0.0
    return f"{rounded:.3f}"


def _consonant_label(spans: list[Span], index: int, vowel: Span) -> str | None:
    """同じモーラの直前区間が子音なら、そのラベル。母音だけと ん は None。"""
    if index <= 0:
        return None
    prev = spans[index - 1]
    if prev.token != vowel.token or not prev.token:
        return None
    if prev.label in VOWEL_ONSET_LABELS or prev.label in (SILENCE_LABEL, BREATH_LABEL):
        return None
    return prev.label


def _overlap_value(settings: OtoSettings, consonant: str | None) -> float:
    if consonant is not None and consonant in settings.overlap_by:
        return settings.overlap_by[consonant]
    return settings.overlap_at


def _overlap_ms(c0: float, v0: float, offset: float, setting: float) -> float:
    """オフセットからのミリ秒。負の setting は子音開始より前の秒で、オフセットより前にも置く。"""
    if setting < 0.0:
        return round((c0 + setting * 1000.0) - offset, 3)
    return round((c0 + (v0 - c0) * setting) - offset, 3)


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
    alias: str | None = None,
) -> str | None:
    """1 エイリアス分の単独音 oto.ini 行。母音でも ん でもなければ None。"""
    settings = settings or OtoSettings()
    vowel = spans[index]
    if vowel.label not in VOWEL_ONSET_LABELS:
        return None
    if alias is None:
        alias = vowel.token or Path(filename).stem
    c0 = _ms(_consonant_start(spans, index, vowel))
    v0 = _ms(vowel.start)
    v1 = _ms(vowel.end)
    dur = max(_ms(duration), v1, c0)

    offset = max(0.0, round(c0 - settings.offset_ms, 3))
    preutter = max(0.0, round(v0 - offset, 3))
    overlap = _overlap_ms(c0, v0, offset, _overlap_value(settings, _consonant_label(spans, index, vowel)))
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


def _vowel_letter(label: str) -> str:
    """連続音エイリアスの、直前の母音。ん は n。"""
    if label in ("N", "M"):
        return "n"
    return label


def _is_rest_token(token: str) -> bool:
    return bool(token) and set(token) <= {"_"}


def _previous_vowel_letter(spans: list[Span], index: int) -> str:
    """直前の母音。休符を越えたときと、ファイルの先頭は -。"""
    for cursor in range(index - 1, -1, -1):
        span = spans[cursor]
        if span.label in VOWEL_ONSET_LABELS:
            return _vowel_letter(span.label)
        if span.label in (SILENCE_LABEL, BREATH_LABEL) and _is_rest_token(span.token):
            return "-"
    return "-"


def _renzoku_alias(filename: str, spans: list[Span], index: int, vowel: Span) -> str:
    alias = vowel.token or Path(filename).stem or vowel.label
    return f"{_previous_vowel_letter(spans, index)} {alias}"


def _follows_rest(spans: list[Span], index: int, vowel: Span) -> bool:
    """このモーラの直前が、アンダーバーだけの無音か。子音と っ はそのモーラとして飛ばす。"""
    cursor = index - 1
    while cursor >= 0:
        span = spans[cursor]
        if (
            span.token == vowel.token
            and span.token
            and span.label not in VOWEL_ONSET_LABELS
            and span.label not in (SILENCE_LABEL, BREATH_LABEL)
        ):
            cursor -= 1
            continue
        break
    if cursor < 0:
        return False
    prev = spans[cursor]
    return prev.label == SILENCE_LABEL and _is_rest_token(prev.token)


def _phoneme_start(spans: list[Span], index: int, vowel: Span) -> float:
    """その音素の開始。連続音のオーバーラップの 100%。

    子音があるときは、その子音と直前の母音の境界。
    子音がないときは、母音と母音の境界。直前の母音が無いときは、その開始そのもの。
    オフセットの 0 は、この点から RENZOKU_OFFSET_LEAD_MS だけ手前です。
    """
    return _consonant_start(spans, index, vowel)


def renzoku_oto_line(
    filename: str,
    spans: list[Span],
    index: int,
    duration: float,
    settings: OtoSettings | None = None,
    loudness: np.ndarray | None = None,
) -> str | None:
    """1 エイリアス分の連続音 oto.ini 行。母音でも ん でもなければ None。"""
    settings = settings or OtoSettings()
    vowel = spans[index]
    if vowel.label not in VOWEL_ONSET_LABELS:
        return None
    if _follows_rest(spans, index, vowel):
        return mono_oto_line(
            filename,
            spans,
            index,
            duration,
            settings.rest or OtoSettings(),
            loudness,
            _renzoku_alias(filename, spans, index, vowel),
        )
    start = _ms(_phoneme_start(spans, index, vowel))
    v0 = _ms(vowel.start)
    v1 = _ms(vowel.end)
    dur = max(_ms(duration), v1, start)

    offset = max(0.0, round(start - RENZOKU_OFFSET_LEAD_MS - settings.offset_ms, 3))
    preutter = max(0.0, round(v0 - offset, 3))
    # 0% はオフセットの位置、100% は音素の開始。
    overlap = round((start - offset) * settings.overlap_at, 3)
    consonant = max(0.0, round((v0 + settings.consonant_ms) - offset, 3))
    blank_at = _ms(
        release_blank_seconds(loudness, duration, spans, index, settings.blank_at)
    )
    blank, consonant = _place_blank(blank_at, offset, consonant, dur)
    alias = _renzoku_alias(filename, spans, index, vowel)
    return (
        f"{filename}={alias},{_fmt(offset)},{_fmt(consonant)},{_fmt(blank)},{_fmt(preutter)},{_fmt(overlap)}"
    )


def renzoku_oto_lines(
    filename: str,
    spans: list[Span],
    duration: float,
    settings: OtoSettings | None = None,
    loudness: np.ndarray | None = None,
) -> list[str]:
    lines: list[str] = []
    for index in range(len(spans)):
        line = renzoku_oto_line(filename, spans, index, duration, settings, loudness)
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
