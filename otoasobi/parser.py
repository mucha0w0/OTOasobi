from __future__ import annotations

import unicodedata
from dataclasses import dataclass

SMALL_YOON = set("ゃゅょャュョ")
SMALL_VOWEL = set("ぁぃぅぇぉァィゥェォ")
SMALL_KANA = SMALL_YOON | SMALL_VOWEL
HIRAGANA = set("あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわゐゑをんがぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽぁぃぅぇぉゃゅょっゎゔ")
KATAKANA = set("アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヰヱヲンガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポァィゥェォャュョッヮヴ")

VOWELS = set("あいうえおをアイウエオ")

_KATA2HIRA = str.maketrans(
    "ァアィイゥウェエォオカガキギクグケゲコゴサザシジスズセゼソゾタダチヂッツヅテデトドナニヌネノハバパヒビピフブプヘベペホボポマミムメモャヤュユョヨラリルレロヮワヰヱヲンヴヵヶ",
    "ぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞただちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽまみむめもゃやゅゆょよらりるれろゎわゐゑをんゔかけ",
)
_HIRA2KATA = str.maketrans(
    "ぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞただちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽまみむめもゃやゅゆょよらりるれろゎわゐゑをんゔ",
    "ァアィイゥウェエォオカガキギクグケゲコゴサザシジスズセゼソゾタダチヂッツヅテデトドナニヌネノハバパヒビピフブプヘベペホボポマミムメモャヤュユョヨラリルレロヮワヰヱヲンヴ",
)
_SMALL_TO_VOWEL = {
    "ぁ": "a",
    "ぃ": "i",
    "ぅ": "u",
    "ぇ": "e",
    "ぉ": "o",
    "ゃ": "a",
    "ゅ": "u",
    "ょ": "o",
}
_I_PALATAL = {
    "k": "ky",
    "g": "gy",
    "n": "ny",
    "h": "hy",
    "m": "my",
    "r": "ry",
    "b": "by",
    "p": "py",
    "s": "sh",
    "t": "ch",
    "d": "j",
    "z": "j",
}

# mora text -> (kind, consonant or None, vowel or None)
# kind: V, CV, N, Q
MORA_TABLE: dict[str, tuple[str, str | None, str | None]] = {}


def _add_row(kana: str, cons: str | None, vowels: str = "aiueo") -> None:
    chars = list(kana)
    if len(chars) != 5:
        raise ValueError(kana)
    for ch, vow in zip(chars, vowels, strict=True):
        if cons is None:
            MORA_TABLE[ch] = ("V", None, vow)
        else:
            MORA_TABLE[ch] = ("CV", cons, vow)


_add_row("あいうえお", None)
_add_row("かきくけこ", "k")
_add_row("さしすせそ", "s")
MORA_TABLE["し"] = ("CV", "sh", "i")
_add_row("たちつてと", "t")
MORA_TABLE["ち"] = ("CV", "ch", "i")
MORA_TABLE["つ"] = ("CV", "ts", "u")
_add_row("なにぬねの", "n")
_add_row("はひふへほ", "h")
MORA_TABLE["ふ"] = ("CV", "f", "u")
_add_row("まみむめも", "m")
MORA_TABLE["や"] = ("CV", "y", "a")
MORA_TABLE["ゆ"] = ("CV", "y", "u")
MORA_TABLE["よ"] = ("CV", "y", "o")
_add_row("らりるれろ", "r")
MORA_TABLE["わ"] = ("CV", "w", "a")
MORA_TABLE["を"] = ("V", None, "o")
MORA_TABLE["ん"] = ("N", "N", None)
MORA_TABLE["っ"] = ("Q", "cl", None)
_add_row("がぎぐげご", "g")
_add_row("ざじずぜぞ", "z")
MORA_TABLE["じ"] = ("CV", "j", "i")
_add_row("だぢづでど", "d")
MORA_TABLE["ぢ"] = ("CV", "j", "i")
MORA_TABLE["づ"] = ("CV", "z", "u")
_add_row("ばびぶべぼ", "b")
_add_row("ぱぴぷぺぽ", "p")

_YOON = {
    "きゃ": ("ky", "a"),
    "きゅ": ("ky", "u"),
    "きょ": ("ky", "o"),
    "しゃ": ("sh", "a"),
    "しゅ": ("sh", "u"),
    "しょ": ("sh", "o"),
    "ちゃ": ("ch", "a"),
    "ちゅ": ("ch", "u"),
    "ちょ": ("ch", "o"),
    "にゃ": ("ny", "a"),
    "にゅ": ("ny", "u"),
    "にょ": ("ny", "o"),
    "ひゃ": ("hy", "a"),
    "ひゅ": ("hy", "u"),
    "ひょ": ("hy", "o"),
    "みゃ": ("my", "a"),
    "みゅ": ("my", "u"),
    "みょ": ("my", "o"),
    "りゃ": ("ry", "a"),
    "りゅ": ("ry", "u"),
    "りょ": ("ry", "o"),
    "ぎゃ": ("gy", "a"),
    "ぎゅ": ("gy", "u"),
    "ぎょ": ("gy", "o"),
    "じゃ": ("j", "a"),
    "じゅ": ("j", "u"),
    "じょ": ("j", "o"),
    "びゃ": ("by", "a"),
    "びゅ": ("by", "u"),
    "びょ": ("by", "o"),
    "ぴゃ": ("py", "a"),
    "ぴゅ": ("py", "u"),
    "ぴょ": ("py", "o"),
}


def _script_forms(text: str) -> set[str]:
    """Hiragana, katakana, and mixed-script spellings of one mora.

    「ヴ」is the one katakana that shows up inside an otherwise hiragana
    filename, so 「ヴぃ」 must be a table key and not only a folded lookup.
    """
    hira = text.translate(_KATA2HIRA)
    kata = hira.translate(_HIRA2KATA)
    forms = {hira, kata}
    if len(hira) == len(kata) == 2:
        forms.add(kata[0] + hira[1])
        forms.add(hira[0] + kata[1])
    return {form for form in forms if form}


def _add_extra(text: str, cons: str, vow: str) -> None:
    entry = ("CV", cons, vow)
    for form in _script_forms(text):
        MORA_TABLE[form] = entry


for _text, (_c, _v) in _YOON.items():
    _add_extra(_text, _c, _v)


_EXTRAS = {
    "いぇ": ("y", "e"),
    "うぁ": ("w", "a"),
    "うぃ": ("w", "i"),
    "うぇ": ("w", "e"),
    "うぉ": ("w", "o"),
    "きぇ": ("ky", "e"),
    "ぎぇ": ("gy", "e"),
    "くぁ": ("kw", "a"),
    "くぃ": ("kw", "i"),
    "くぇ": ("kw", "e"),
    "くぉ": ("kw", "o"),
    "くゎ": ("kw", "a"),
    "ぐぁ": ("gw", "a"),
    "ぐぃ": ("gw", "i"),
    "ぐぇ": ("gw", "e"),
    "ぐぉ": ("gw", "o"),
    "すぃ": ("s", "i"),
    "ずぃ": ("z", "i"),
    "しぇ": ("sh", "e"),
    "じぇ": ("j", "e"),
    "ちぇ": ("ch", "e"),
    "つぁ": ("ts", "a"),
    "つぃ": ("ts", "i"),
    "つぇ": ("ts", "e"),
    "つぉ": ("ts", "o"),
    "てぃ": ("t", "i"),
    "てゅ": ("ty", "u"),
    "とぅ": ("t", "u"),
    "でぃ": ("d", "i"),
    "でゅ": ("dy", "u"),
    "どぅ": ("d", "u"),
    "にぇ": ("ny", "e"),
    "ひぇ": ("hy", "e"),
    "ふぁ": ("f", "a"),
    "ふぃ": ("f", "i"),
    "ふぇ": ("f", "e"),
    "ふぉ": ("f", "o"),
    "ふゃ": ("fy", "a"),
    "ふゅ": ("fy", "u"),
    "ふょ": ("fy", "o"),
    "みぇ": ("my", "e"),
    "りぇ": ("ry", "e"),
    "びぇ": ("by", "e"),
    "ぴぇ": ("py", "e"),
    "ゔ": ("v", "u"),
    "ゔぁ": ("v", "a"),
    "ゔぃ": ("v", "i"),
    "ゔぇ": ("v", "e"),
    "ゔぉ": ("v", "o"),
    "ゔゃ": ("vy", "a"),
    "ゔゅ": ("vy", "u"),
    "ゔょ": ("vy", "o"),
}
for _text, (_c, _v) in _EXTRAS.items():
    _add_extra(_text, _c, _v)

# Sibilants. The CV cut is the drop in energy above 3.5 kHz.
SIBILANT = {"s", "sh", "z", "j", "ts", "ch"}
# [h]/[ç] (はひへほ・ひゃ行) and [ɸ] (ふ・ふゃ行). That high band is weak or
# missing, and the consonant is often quieter than the vowel, so the sibilant
# ratio test marks one frame or finds no island and skips the row.
WEAK_FRICATIVE = {"h", "hy", "f", "fy"}
FRICATIVE = SIBILANT | WEAK_FRICATIVE
# Japanese ヴ is a voiced labial [β]. Its boundary is the rise into the vowel.
LABIAL_FRICATIVE = {"v", "vy"}
STOP = {"k", "t", "p", "b", "d", "g", "ky", "gy", "py", "by", "ty", "dy", "kw", "gw"}
NASAL = {"n", "ny", "m", "my"}
# r/ry are flaps. y (や行) and w (わ行) share the same short-consonant boundary.
FLAP = {"r", "ry", "y", "w"}


def _hira(text: str) -> str:
    return text.translate(_KATA2HIRA)


def _normalize_stem(text: str) -> str:
    """Fold the spellings of ヴ that filenames actually use.

    Halfwidth ``ｳﾞ`` and the spacing dakuten ``ウ゛`` / ``う゛`` are not
    precomposed ヴ / ゔ. NFKC turns the spacing mark into a space plus a
    combiner, so attach that mark before folding.
    """
    widened: list[str] = []
    for ch in text:
        if 0xFF61 <= ord(ch) <= 0xFF9F:
            widened.append(unicodedata.normalize("NFKC", ch))
        else:
            widened.append(ch)
    out: list[str] = []
    for ch in widened:
        if ch in "\u309b\u309c" and out and 0x3040 <= ord(out[-1]) <= 0x30FF:
            mark = "\u3099" if ch == "\u309b" else "\u309a"
            out[-1] = unicodedata.normalize("NFC", out[-1] + mark)
        else:
            out.append(ch)
    return unicodedata.normalize("NFC", "".join(out))


def _lookup_mora(text: str) -> tuple[str, str | None, str | None] | None:
    entry = MORA_TABLE.get(text) or MORA_TABLE.get(_hira(text))
    return entry


def _palatalize(cons: str) -> str:
    if cons in _I_PALATAL:
        return _I_PALATAL[cons]
    if cons.endswith("y") or cons in {"sh", "ch", "j"}:
        return cons
    return cons + "y"


def _combine_with_small(base: str, small: str) -> tuple[str, str | None, str] | None:
    """Attach a small kana to a base mora (と+ぅ → tu, り+ぇ → rye)."""
    small = _hira(small)
    if small not in _SMALL_TO_VOWEL:
        return None
    entry = _lookup_mora(base)
    if entry is None:
        return None
    kind, cons, vow = entry
    if kind not in {"V", "CV"}:
        return None
    new_vow = _SMALL_TO_VOWEL[small]
    is_yoon = small in "ゃゅょ"
    is_small_e = small == "ぇ"

    if cons is None:
        if vow == "i" and (is_yoon or is_small_e):
            return ("CV", "y", new_vow)
        if vow == "u" and small in "ぁぃぇぉ":
            return ("CV", "w", new_vow)
        return None

    if vow == "i" and (is_yoon or is_small_e):
        return ("CV", _palatalize(cons), new_vow)
    if is_yoon:
        if cons in {"t", "d"}:
            return ("CV", cons + "y", new_vow)
        if cons in {"f", "v"}:
            return ("CV", cons + "y", new_vow)
        if cons in {"k", "g"} and vow == "u":
            return ("CV", cons + "w", new_vow)
        return ("CV", _palatalize(cons) if vow == "i" else cons, new_vow)
    if cons in {"k", "g"} and vow == "u" and small in "ぁぃぇぉ":
        return ("CV", cons + "w", new_vow)
    if new_vow == vow:
        return None
    return ("CV", cons, new_vow)


@dataclass(frozen=True)
class Token:
    kind: str  # rest, V, CV, N, Q
    text: str
    consonant: str | None = None
    vowel: str | None = None

    @property
    def is_rest(self) -> bool:
        return self.kind == "rest"

    @property
    def needs_cv_split(self) -> bool:
        return self.kind == "CV" and self.consonant is not None and self.vowel is not None

    @property
    def is_fricative(self) -> bool:
        return self.consonant in SIBILANT

    @property
    def is_weak_fricative(self) -> bool:
        return self.consonant in WEAK_FRICATIVE

    @property
    def is_labial_fricative(self) -> bool:
        return self.consonant in LABIAL_FRICATIVE

    @property
    def is_stop(self) -> bool:
        return self.kind == "Q" or self.consonant in STOP

    @property
    def is_nasal(self) -> bool:
        return self.kind == "N" or self.consonant in NASAL

    @property
    def is_flap(self) -> bool:
        return self.consonant in FLAP

    @property
    def is_vowel_only(self) -> bool:
        return self.kind == "V"


class FilenameError(ValueError):
    pass


def stem_from_path(path: str) -> str:
    name = path.replace("\\", "/").rsplit("/", 1)[-1]
    if name.lower().endswith(".wav"):
        name = name[:-4]
    return name


def parse_stem(stem: str) -> list[Token]:
    if not stem:
        raise FilenameError("ファイル名が空です")
    stem = _normalize_stem(stem)
    tokens: list[Token] = []
    i = 0
    n = len(stem)
    while i < n:
        ch = stem[i]
        if ch == "_":
            j = i
            while j < n and stem[j] == "_":
                j += 1
            tokens.append(Token("rest", stem[i:j]))
            i = j
            continue
        if i + 1 < n:
            two = stem[i : i + 2]
            entry = _lookup_mora(two)
            if entry is None and two[1] in SMALL_KANA:
                entry = _combine_with_small(two[0], two[1])
            if entry is not None and (
                two in MORA_TABLE or _hira(two) in MORA_TABLE or two[1] in SMALL_KANA
            ):
                kind, cons, vow = entry
                tokens.append(Token(kind, two, cons, vow))
                i += 2
                continue
        entry = _lookup_mora(ch)
        if entry is not None:
            kind, cons, vow = entry
            tokens.append(Token(kind, ch, cons, vow))
            i += 1
            continue
        if ch in SMALL_VOWEL:
            tokens.append(Token("V", ch, None, _SMALL_TO_VOWEL[_hira(ch)]))
            i += 1
            continue
        if ch in SMALL_YOON:
            raise FilenameError(f"単独の小書き仮名は使えません: {ch}")
        raise FilenameError(f"使えない文字です: {ch!r}")
    if not tokens:
        raise FilenameError("トークンがありません")
    return tokens


def speech_chunks(tokens: list[Token]) -> list[list[Token]]:
    chunks: list[list[Token]] = []
    current: list[Token] = []
    for token in tokens:
        if token.is_rest:
            if current:
                chunks.append(current)
                current = []
        else:
            current.append(token)
    if current:
        chunks.append(current)
    return chunks


@dataclass(frozen=True)
class MoraGroup:
    """One mora sent to analysis. Extra parts continue the same vowel."""

    parts: tuple[Token, ...]
    indices: tuple[int, ...]

    @property
    def head(self) -> Token:
        return self.parts[0]

    @property
    def index(self) -> int:
        return self.indices[0]


def group_moras(tokens: list[Token]) -> list[MoraGroup]:
    """Collapse a run of the same vowel into one analysis mora.

    「ああ」 is aligned as a single vowel. A vowel-only mora that keeps going
    with that same vowel is included (「かあ」 as well as 「あああ」). The
    span is split into equal durations afterward, one slice per original mora.
    """
    groups: list[MoraGroup] = []
    i = 0
    n = len(tokens)
    while i < n:
        token = tokens[i]
        parts = [token]
        indices = [i]
        j = i + 1
        if token.vowel is not None and not token.is_rest:
            while j < n and tokens[j].is_vowel_only and tokens[j].vowel == token.vowel:
                parts.append(tokens[j])
                indices.append(j)
                j += 1
        groups.append(MoraGroup(tuple(parts), tuple(indices)))
        i = j
    return groups


def speech_group_chunks(tokens: list[Token]) -> list[list[MoraGroup]]:
    chunks: list[list[MoraGroup]] = []
    current: list[MoraGroup] = []
    for group in group_moras(tokens):
        if group.head.is_rest:
            if current:
                chunks.append(current)
                current = []
        else:
            current.append(group)
    if current:
        chunks.append(current)
    return chunks


def rest_layout(tokens: list[Token]) -> tuple[bool, bool, list[Token]]:
    """Leading / trailing / internal rest tokens from a filename parse."""
    leading = bool(tokens) and tokens[0].is_rest
    trailing = bool(tokens) and tokens[-1].is_rest
    rests = [token for token in tokens if token.is_rest]
    internal = rests[1:] if leading else list(rests)
    if trailing and internal:
        internal = internal[:-1]
    return leading, trailing, internal
