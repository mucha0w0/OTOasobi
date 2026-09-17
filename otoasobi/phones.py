from __future__ import annotations

from dataclasses import dataclass

from otoasobi.parser import Token

# MMS-FA letters only. し/しゃ are [s], ち is [c], つ is [t s], ん is [n], っ is [q].
_CONS_MMS: dict[str, list[str]] = {
    "s": ["s"],
    "sh": ["s"],
    "z": ["z"],
    "j": ["j"],
    "k": ["k"],
    "ky": ["k", "y"],
    "t": ["t"],
    "ch": ["c"],
    "ts": ["t", "s"],
    "n": ["n"],
    "ny": ["n", "y"],
    "h": ["h"],
    "hy": ["h", "y"],
    "f": ["f"],
    "m": ["m"],
    "my": ["m", "y"],
    "r": ["r"],
    "ry": ["r", "y"],
    "w": ["w"],
    "y": ["y"],
    "g": ["g"],
    "gy": ["g", "y"],
    "d": ["d"],
    "b": ["b"],
    "by": ["b", "y"],
    "p": ["p"],
    "py": ["p", "y"],
    "v": ["v"],
    "N": ["n"],
    "cl": ["q"],
}

_VOWELS = set("aiueo")


@dataclass(frozen=True)
class MmsUnit:
    char: str
    token_index: int
    role: str  # C or V


def _cons_letters(consonant: str | None) -> list[str]:
    if not consonant:
        return []
    if consonant in _CONS_MMS:
        return list(_CONS_MMS[consonant])
    if consonant[0] in set("aienoutsrmkldghybwpwcvjzfqx'"):
        return [consonant[0]]
    return []


def token_mms_units(token: Token, token_index: int) -> list[MmsUnit]:
    if token.is_rest:
        return []
    if token.kind == "V":
        vow = token.vowel if token.vowel in _VOWELS else "a"
        return [MmsUnit(vow, token_index, "V")]
    if token.kind == "N":
        return [MmsUnit("n", token_index, "C")]
    if token.kind == "Q":
        return [MmsUnit("q", token_index, "C")]
    units = [MmsUnit(ch, token_index, "C") for ch in _cons_letters(token.consonant)]
    if token.vowel in _VOWELS:
        units.append(MmsUnit(token.vowel, token_index, "V"))
    return units


def mms_plan(tokens: list[Token]) -> list[MmsUnit]:
    plan: list[MmsUnit] = []
    for i, token in enumerate(tokens):
        plan.extend(token_mms_units(token, i))
    return plan
