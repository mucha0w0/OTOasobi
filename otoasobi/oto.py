"""単独音、連続音、CVVC の oto.ini。

1 行は次の形です。値はミリ秒、小数第 3 位です。

    ファイル名.wav=エイリアス,オフセット,子音部,ブランク,先行発声,オーバーラップ

オフセットはファイル先頭から。子音部・先行発声・オーバーラップはオフセットから。
ブランクは負の値で書き、オフセットからの時間に -1 を掛けたものです。
子音部とブランクは、単独音も連続音も同じ位置です。
先行発声は、単独音と連続音と CVVC の通常音素では母音の開始です。

単独音のオフセットは、子音の開始を基準に ±0.1 秒だけ前後へずらします。
単独音のオーバーラップは、0 が子音の開始、1 が母音の開始です。
負の数は、子音の開始より前の秒です。その位置がオフセットより前でも、そのまま置きます。
連続音のオフセットの 0 は、その音素の子音と直前の母音の境界から 0.15 秒手前です。
子音がない音素は、母音と母音の境界から 0.15 秒手前です。ずらす幅は単独音と同じです。
連続音のオーバーラップは、オフセットの位置を 0%、その音素の開始を 100% とした割合です。
音素の開始は、子音と直前の母音の境界です。子音がないときは、母音と母音の境界です。
アンダーバーだけの無音の直後の音素は、単独音と同じ計算です。値は rest のプリセットです。
エイリアスは連続音のままです。

CVVC は、母音の音素ごとに通常音の行を書き、先頭以外で子音があるときはその前に子音音素の行を書きます。
「さしす」は「- さ」「a sh」「し」「i s」「す」です。
先頭音素は、ファイルの先頭と、アンダーバーだけの無音の直後です。計算は lead の単独音です。
あいうえおんは、アンダーバーの直後を除き、連続音と同じ計算です。エイリアスは CVVC のままです。
ファイルの先頭は「- あ」、それ以外は「あ」です。オフセットとオーバーラップは子音音素の値、子音部とブランクは通常音素の値です。
通常音素のオフセットは子音の開始です。オーバーラップは、その位置を 0%、先行発声を 100% とした割合で、0 未満は使いません。
子音音素のエイリアスは、直前の母音と現在の子音です。オフセットとオーバーラップは連続音と同じです。
子音音素の先行発声は、その子音の開始です。
ブランクは子音の開始を 0%、終了を 100% とした割合です。
子音部は、先行発声を 0%、そのブランクの位置を 100% とした割合です。

同じエイリアスが 2 回以上あるときは、2 回目以降の末尾に 2、3、… を付けます。
1 回目はそのままです。その番号が別のエイリアスと重なるときは、空くまで進めます。
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
# 連続音のオフセットの 0。音素の開始から手前へこの幅。CVVC の子音音素も同じ。
RENZOKU_OFFSET_LEAD_MS = 150.0
# CVVC の子音音素。子音部もブランクも、スライダーの初期値は 100%。
VC_PART_AT = 1.0
# CVVC で連続音の計算にする、子音のない母音。を と小書き母音も同じ。
_CVVC_VOWEL_PHONEMES = frozenset("あいうえおんをぁぃぅぇぉアイウエオンヲァィゥェォ")
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
    # mono、renzoku、cvvc。
    mode: str = "mono"
    # 連続音で、アンダーバーの無音の直後にだけ使う単独音の値。
    rest: OtoSettings | None = None
    # CVVC の通常音素。0〜1 だけ。無い子音は overlap_at。子音部・ブランクは上の欄。
    cvvc_overlap_by: dict[str, float] = field(default_factory=dict)
    # CVVC の子音音素。オフセットは連続音と同じずらし。子音部とブランクは 0〜1。
    vc_offset_ms: float = 0.0
    vc_consonant_by: dict[str, float] = field(default_factory=dict)
    vc_blank_by: dict[str, float] = field(default_factory=dict)
    vc_overlap_at: float = OVERLAP_AT
    # CVVC の先頭音素に使う単独音。
    lead: OtoSettings | None = None


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


def _ratio_by(raw: object, default: float = OVERLAP_AT) -> dict[str, float]:
    """子音ラベルごとの 0〜1。負の秒にはしない。"""
    if not isinstance(raw, dict):
        return {}
    table: dict[str, float] = {}
    for key, value in raw.items():
        if isinstance(key, str) and key:
            table[key] = _bounded(value, default, 0.0, 1.0)
    return table


def _mode_name(raw: object) -> str:
    if raw == "renzoku":
        return "renzoku"
    if raw == "cvvc":
        return "cvvc"
    return "mono"


def oto_settings_from(raw: object, *, with_rest: bool = True) -> OtoSettings:
    """画面からの config。無い欄と範囲外は初期値か、許す範囲の端に寄せる。"""
    if not isinstance(raw, dict):
        return OtoSettings()
    mode = _mode_name(raw.get("mode"))
    overlap_raw = raw.get("overlap")
    cvvc_overlap_by: dict[str, float] = {}
    if mode == "cvvc":
        overlap_at = OVERLAP_AT
        overlap_by = {}
        cvvc_overlap_by = _ratio_by(overlap_raw)
    elif isinstance(overlap_raw, dict):
        overlap_at = OVERLAP_AT
        overlap_by = _overlap_by(overlap_raw)
    else:
        overlap_at = _bounded(overlap_raw, OVERLAP_AT, 0.0, 1.0)
        overlap_by = {}
    rest = None
    lead = None
    if with_rest and isinstance(raw.get("rest"), dict):
        rest = oto_settings_from(raw["rest"], with_rest=False)
    if with_rest and isinstance(raw.get("lead"), dict):
        lead = oto_settings_from(raw["lead"], with_rest=False)
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
        mode=mode,
        rest=rest,
        cvvc_overlap_by=cvvc_overlap_by,
        vc_offset_ms=_bounded(
            raw.get("vc_offset_ms"),
            0.0,
            -MAX_OFFSET_SHIFT_MS,
            MAX_OFFSET_SHIFT_MS,
        ),
        vc_consonant_by=_ratio_by(raw.get("vc_consonant"), VC_PART_AT),
        vc_blank_by=_ratio_by(raw.get("vc_blank"), VC_PART_AT),
        vc_overlap_at=_bounded(raw.get("vc_overlap"), OVERLAP_AT, 0.0, 1.0),
        lead=lead,
    )


def _ms(seconds: float) -> float:
    return round(float(seconds) * 1000.0, 3)


def _fmt(value: float) -> str:
    rounded = round(float(value), 3)
    if abs(rounded) < 0.0005:
        rounded = 0.0
    return f"{rounded:.3f}"


def _consonant_span(spans: list[Span], index: int, vowel: Span) -> Span | None:
    """同じモーラの直前区間が子音なら、その区間。母音だけと ん は None。"""
    if index <= 0:
        return None
    prev = spans[index - 1]
    if prev.token != vowel.token or not prev.token:
        return None
    if prev.label in VOWEL_ONSET_LABELS or prev.label in (SILENCE_LABEL, BREATH_LABEL):
        return None
    return prev


def _consonant_label(spans: list[Span], index: int, vowel: Span) -> str | None:
    span = _consonant_span(spans, index, vowel)
    if span is None:
        return None
    return span.label


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


def adjustments_from(raw: object) -> list[dict]:
    """画面でドラッグした位置。file, index, alias と、直した点だけの秒。"""
    if not isinstance(raw, list):
        return []
    found: list[dict] = []
    for item in raw:
        if not isinstance(item, dict):
            continue
        filename = item.get("file")
        alias = item.get("alias")
        index = item.get("index")
        if not isinstance(filename, str) or not filename or len(filename) > 255:
            continue
        if not isinstance(alias, str) or not alias.strip() or len(alias) > 80:
            continue
        if isinstance(index, bool) or not isinstance(index, (int, float)):
            continue
        if isinstance(index, float) and not index.is_integer():
            continue
        index = int(index)
        if index < 0 or index > 10000:
            continue
        points: dict[str, float] = {}
        for key in ("offset", "overlap", "preutter", "consonant", "blank"):
            value = item.get(key)
            if isinstance(value, bool) or not isinstance(value, (int, float)):
                continue
            number = float(value)
            if number != number or number in (float("inf"), float("-inf")):
                continue
            points[key] = max(-0.1, min(36000.0, number))
        if not points:
            continue
        found.append(
            {
                "file": filename,
                "index": index,
                "alias": alias.strip(),
                "points": points,
            }
        )
    return found


def _split_oto_values(line: str) -> tuple[str, str, float, float, float, float, float] | None:
    """ファイル名、エイリアス、オフセット、子音部、ブランク、先行発声、オーバーラップ。ミリ秒。"""
    parsed = _split_oto_alias(line)
    if parsed is None:
        return None
    filename, alias, tail = parsed
    parts = tail.split(",")
    if len(parts) != 6:
        return None
    try:
        offset, consonant, blank, preutter, overlap = (float(part) for part in parts[1:])
    except ValueError:
        return None
    return filename, alias, offset, consonant, blank, preutter, overlap


def _apply_line(line: str, expected_alias: str, points: dict[str, float], duration: float) -> str:
    """直した絶対時刻だけを載せ、残りは計算した位置のままにする。"""
    parsed = _split_oto_values(line)
    if parsed is None:
        return line
    filename, alias, offset, consonant, blank, preutter, overlap = parsed
    if alias != expected_alias:
        return line
    off = offset
    pre_at = offset + preutter
    con_at = offset + consonant
    end = offset - blank
    ov_at = offset + overlap
    if "offset" in points:
        off = _ms(points["offset"])
    if "preutter" in points:
        pre_at = _ms(points["preutter"])
    if "consonant" in points:
        con_at = _ms(points["consonant"])
    if "blank" in points:
        end = _ms(points["blank"])
    if "overlap" in points:
        ov_at = _ms(points["overlap"])
    off = max(0.0, round(off, 3))
    dur = max(_ms(max(duration, 0.0)), off, end, con_at, pre_at, ov_at)
    pre_at = max(off, round(pre_at, 3))
    con_rel = max(0.0, round(con_at - off, 3))
    blank_rel, con_rel = _place_blank(round(end, 3), off, con_rel, dur)
    pre_rel = max(0.0, round(pre_at - off, 3))
    ov_at = min(max(round(ov_at, 3), -100.0), dur)
    ov_rel = round(ov_at - off, 3)
    return (
        f"{filename}={alias},{_fmt(off)},{_fmt(con_rel)},{_fmt(blank_rel)},{_fmt(pre_rel)},{_fmt(ov_rel)}"
    )


def apply_file_adjustments(
    lines: list[str],
    filename: str,
    adjustments: list[dict] | None,
    duration: float,
) -> list[str]:
    """1 ファイル分の行へ、同じ並びのドラッグ位置を載せる。エイリアスが違う行は触らない。"""
    if not adjustments:
        return lines
    by_index: dict[int, dict] = {}
    for item in adjustments:
        if item.get("file") != filename:
            continue
        index = item.get("index")
        if isinstance(index, int):
            by_index[index] = item
    if not by_index:
        return lines
    out: list[str] = []
    for index, line in enumerate(lines):
        item = by_index.get(index)
        if item is None:
            out.append(line)
            continue
        out.append(_apply_line(line, str(item.get("alias") or ""), item["points"], duration))
    return out


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


def _renzoku_timed_line(
    filename: str,
    spans: list[Span],
    index: int,
    duration: float,
    settings: OtoSettings,
    loudness: np.ndarray | None,
    alias: str,
) -> str:
    """連続音の位置。オフセットは音素の開始から 0.15 秒手前、オーバーラップはそのあいだの割合。"""
    vowel = spans[index]
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
    return (
        f"{filename}={alias},{_fmt(offset)},{_fmt(consonant)},{_fmt(blank)},{_fmt(preutter)},{_fmt(overlap)}"
    )


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
    alias = _renzoku_alias(filename, spans, index, vowel)
    if _follows_rest(spans, index, vowel):
        return mono_oto_line(
            filename,
            spans,
            index,
            duration,
            settings.rest or OtoSettings(),
            loudness,
            alias,
        )
    return _renzoku_timed_line(filename, spans, index, duration, settings, loudness, alias)


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


def _ratio(table: dict[str, float], key: str, default: float) -> float:
    if key in table:
        return table[key]
    return default


def _mora_alias(filename: str, vowel: Span) -> str:
    return vowel.token or Path(filename).stem or vowel.label


def _is_leading(spans: list[Span], index: int) -> bool:
    """発音の先頭。ファイルの先頭と、アンダーバーだけの無音の直後。"""
    return _previous_vowel_letter(spans, index) == "-"


def _is_vowel_phoneme(token: str) -> bool:
    """子音のない母音。あいうえおん、を、小書き母音。平仮名でも片仮名でも。"""
    return token in _CVVC_VOWEL_PHONEMES


def _cvvc_vowel_settings(settings: OtoSettings) -> OtoSettings:
    """母音音素の連続音計算。ずらしと割合は子音音素、子音部とブランクは通常音素。"""
    return OtoSettings(
        offset_ms=settings.vc_offset_ms,
        consonant_ms=settings.consonant_ms,
        blank_at=settings.blank_at,
        overlap_at=settings.vc_overlap_at,
    )


def _cvvc_normal_settings(settings: OtoSettings) -> OtoSettings:
    """通常音素。オフセットは子音の開始に固定し、オーバーラップは 0〜1 だけ。"""
    return OtoSettings(
        offset_ms=0.0,
        consonant_ms=settings.consonant_ms,
        blank_at=settings.blank_at,
        overlap_at=OVERLAP_AT,
        overlap_by=settings.cvvc_overlap_by,
    )


def cvvc_vc_line(
    filename: str,
    spans: list[Span],
    index: int,
    duration: float,
    settings: OtoSettings | None = None,
) -> str | None:
    """子音音素の 1 行。子音が無いモーラは None。"""
    settings = settings or OtoSettings()
    vowel = spans[index]
    cons = _consonant_span(spans, index, vowel)
    if cons is None:
        return None
    c0 = _ms(cons.start)
    c1 = _ms(cons.end)
    if c1 < c0:
        c1 = c0
    dur = max(_ms(duration), c1, c0)
    offset = max(0.0, round(c0 - RENZOKU_OFFSET_LEAD_MS - settings.vc_offset_ms, 3))
    preutter = max(0.0, round(c0 - offset, 3))
    overlap = round((c0 - offset) * settings.vc_overlap_at, 3)
    blank_at = round(c0 + (c1 - c0) * _ratio(settings.vc_blank_by, cons.label, VC_PART_AT), 3)
    fixed_at = round(c0 + (blank_at - c0) * _ratio(settings.vc_consonant_by, cons.label, VC_PART_AT), 3)
    consonant = max(0.0, round(fixed_at - offset, 3))
    blank, consonant = _place_blank(blank_at, offset, consonant, dur)
    alias = f"{_previous_vowel_letter(spans, index)} {cons.label}"
    return (
        f"{filename}={alias},{_fmt(offset)},{_fmt(consonant)},{_fmt(blank)},{_fmt(preutter)},{_fmt(overlap)}"
    )


def cvvc_oto_lines(
    filename: str,
    spans: list[Span],
    duration: float,
    settings: OtoSettings | None = None,
    loudness: np.ndarray | None = None,
) -> list[str]:
    """CVVC の oto.ini。先頭、子音音素、通常音素の順に、発音の順で並べる。"""
    settings = settings or OtoSettings()
    normal = _cvvc_normal_settings(settings)
    lead = settings.lead or OtoSettings()
    lines: list[str] = []
    for index, vowel in enumerate(spans):
        if vowel.label not in VOWEL_ONSET_LABELS:
            continue
        alias = _mora_alias(filename, vowel)
        if _is_vowel_phoneme(vowel.token):
            # アンダーバーの直後だけ、連続音と同じく単独音。使う値は先頭音素のプリセット。
            shown = f"- {alias}" if _is_leading(spans, index) else alias
            if _follows_rest(spans, index, vowel):
                line = mono_oto_line(filename, spans, index, duration, lead, loudness, shown)
            else:
                line = _renzoku_timed_line(
                    filename,
                    spans,
                    index,
                    duration,
                    _cvvc_vowel_settings(settings),
                    loudness,
                    shown,
                )
            if line is not None:
                lines.append(line)
            continue
        if _is_leading(spans, index):
            line = mono_oto_line(filename, spans, index, duration, lead, loudness, f"- {alias}")
            if line is not None:
                lines.append(line)
            continue
        vc = cvvc_vc_line(filename, spans, index, duration, settings)
        if vc is not None:
            lines.append(vc)
        line = mono_oto_line(filename, spans, index, duration, normal, loudness, alias)
        if line is not None:
            lines.append(line)
    return lines


def _split_oto_alias(line: str) -> tuple[str, str, str] | None:
    """ファイル名、エイリアス、エイリアスより後ろ。形が違う行は None。"""
    eq = line.find("=")
    if eq < 0:
        return None
    body = line[eq + 1 :]
    comma = body.find(",")
    if comma < 0:
        return None
    return line[:eq], body[:comma], body[comma:]


def number_duplicate_aliases(lines: list[str]) -> list[str]:
    """同じエイリアスの 2 回目以降へ 2、3、… を付ける。

    1 回目の名前は残す。付けた名前が、別のエイリアスの 1 回目や、
    すでに付けた名前と重なるときは、空いている番号まで進める。
    「あ」が 3 行なら「あ」「あ2」「あ3」です。
    """
    parsed = [_split_oto_alias(line) for line in lines]
    reserved = {item[1] for item in parsed if item is not None}
    used: set[str] = set()
    seen: dict[str, int] = {}
    out: list[str] = []
    for line, item in zip(lines, parsed, strict=True):
        if item is None:
            out.append(line)
            continue
        filename, alias, tail = item
        seen[alias] = seen.get(alias, 0) + 1
        if seen[alias] == 1:
            chosen = alias
        else:
            number = 2
            while True:
                candidate = f"{alias}{number}"
                if candidate not in reserved and candidate not in used:
                    chosen = candidate
                    break
                number += 1
        used.add(chosen)
        out.append(f"{filename}={chosen}{tail}")
    return out


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
    path.write_text(
        render_oto_ini(number_duplicate_aliases(lines)),
        encoding=ANSI,
        newline="\r\n",
    )
    return path
