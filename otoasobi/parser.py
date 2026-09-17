from __future__ import annotations

from dataclasses import dataclass

SMALL_YOON = set("ゃゅょャュョ")
SMALL_VOWEL = set("ぁぃぅぇぉァィゥェォ")
HIRAGANA = set("あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわゐゑをんがぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽぁぃぅぇぉゃゅょっゎ")
KATAKANA = set("アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヰヱヲンガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポァィゥェォャュョッヮヴ")

VOWELS = set("あいうえおをアイウエオ")

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
for _text, (_c, _v) in _YOON.items():
    MORA_TABLE[_text] = ("CV", _c, _v)

_KATA = {
    "ヴ": ("CV", "v", "u"),
    "ヴァ": ("CV", "v", "a"),
    "ヴィ": ("CV", "v", "i"),
    "ヴェ": ("CV", "v", "e"),
    "ヴォ": ("CV", "v", "o"),
    "ファ": ("CV", "f", "a"),
    "フィ": ("CV", "f", "i"),
    "フェ": ("CV", "f", "e"),
    "フォ": ("CV", "f", "o"),
    "ティ": ("CV", "t", "i"),
    "ディ": ("CV", "d", "i"),
    "トゥ": ("CV", "t", "u"),
    "ドゥ": ("CV", "d", "u"),
    "チェ": ("CV", "ch", "e"),
    "シェ": ("CV", "sh", "e"),
    "ジェ": ("CV", "j", "e"),
    "ツァ": ("CV", "ts", "a"),
    "ツィ": ("CV", "ts", "i"),
    "ツェ": ("CV", "ts", "e"),
    "ツォ": ("CV", "ts", "o"),
    "ウィ": ("CV", "w", "i"),
    "ウェ": ("CV", "w", "e"),
    "ウォ": ("CV", "w", "o"),
}
MORA_TABLE.update(_KATA)

# hiragana versions of a few foreign syllables
MORA_TABLE["ふぁ"] = ("CV", "f", "a")
MORA_TABLE["ふぃ"] = ("CV", "f", "i")
MORA_TABLE["ふぇ"] = ("CV", "f", "e")
MORA_TABLE["ふぉ"] = ("CV", "f", "o")
MORA_TABLE["てぃ"] = ("CV", "t", "i")
MORA_TABLE["でぃ"] = ("CV", "d", "i")
MORA_TABLE["しぇ"] = ("CV", "sh", "e")
MORA_TABLE["ちぇ"] = ("CV", "ch", "e")
MORA_TABLE["じぇ"] = ("CV", "j", "e")

FRICATIVE = {"s", "sh", "h", "hy", "f", "z", "j", "ts", "ch", "v"}
STOP = {"k", "t", "p", "b", "d", "g", "ky", "gy", "py", "by"}
NASAL = {"n", "ny", "m", "my"}


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
        return self.consonant in FRICATIVE

    @property
    def is_stop(self) -> bool:
        return self.kind == "Q" or self.consonant in STOP

    @property
    def is_nasal(self) -> bool:
        return self.kind == "N" or self.consonant in NASAL


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
        if i + 1 < n and stem[i : i + 2] in MORA_TABLE:
            text = stem[i : i + 2]
            kind, cons, vow = MORA_TABLE[text]
            tokens.append(Token(kind, text, cons, vow))
            i += 2
            continue
        if ch in MORA_TABLE:
            kind, cons, vow = MORA_TABLE[ch]
            tokens.append(Token(kind, ch, cons, vow))
            i += 1
            continue
        if ch in SMALL_YOON or ch in SMALL_VOWEL:
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
