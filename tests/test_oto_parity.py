"""C# の OtoMarks / BlankRelease / OtoTune と、同じ入力のミリ秒が一致する。

tests/oto_parity.json の expected は、このファイルが計算した値。
`python tests/test_oto_parity.py` で expected を書き直す。
"""

from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

import numpy as np

from otoasobi.blank import release_blank_seconds
from otoasobi.oto import OtoSettings, _apply_line, mono_oto_lines, renzoku_oto_lines
from otoasobi.segment import Span

CASES = Path(__file__).with_name("oto_parity.json")


def _spans(raw: list[dict]) -> list[Span]:
    return [Span(float(item["start"]), float(item["end"]), item["label"], item["token"]) for item in raw]


def _loudness(raw: list[float] | None) -> np.ndarray | None:
    if raw is None:
        return None
    return np.asarray(raw, dtype=np.float64)


def _settings(raw: dict, mode: str) -> OtoSettings:
    overlap = raw.get("overlap")
    return OtoSettings(
        offset_ms=float(raw["offsetMs"]),
        consonant_ms=float(raw["consonantMs"]),
        blank_at=float(raw["blank"]),
        overlap_at=float(raw.get("overlapAt", 0.5)),
        overlap_by=dict(overlap) if isinstance(overlap, dict) else {},
        mode=mode,
    )


def compute(doc: dict) -> dict:
    mono = doc["mono"]
    renzoku = doc["renzoku"]
    blank = doc["blank"]
    tunes = []
    for item in doc["tunes"]:
        tunes.append(
            _apply_line(item["line"], item["alias"], {key: float(value) for key, value in item["points"].items()}, float(item["duration"]))
        )
    return {
        "mono": mono_oto_lines(
            mono["file"],
            _spans(mono["spans"]),
            float(mono["duration"]),
            _settings(mono, "mono"),
            _loudness(mono.get("loudness")),
        ),
        "renzoku": renzoku_oto_lines(
            renzoku["file"],
            _spans(renzoku["spans"]),
            float(renzoku["duration"]),
            _settings(renzoku, "renzoku"),
            _loudness(renzoku.get("loudness")),
        ),
        "blank": round(float(release_blank_seconds(
            _loudness(blank["loudness"]),
            float(blank["duration"]),
            _spans(blank["spans"]),
            int(blank["index"]),
            float(blank["shift"]),
        )), 6),
        "tunes": tunes,
    }


def _plateau() -> list[int]:
    drops = {round(0.63 + 0.02 * step, 2): -12 - 4 * step for step in range(8)}
    loud: list[int] = []
    for index in range(50):
        time = round((index + 0.5) / 50, 2)
        if time < 0.12 or time >= 0.78:
            loud.append(-40)
        elif time <= 0.62:
            loud.append(-8)
        else:
            loud.append(drops[time])
    return loud


def write_cases() -> None:
    loud = _plateau()
    vowel = [
        {"start": 0.0, "end": 0.10, "label": "k", "token": "か"},
        {"start": 0.10, "end": 0.70, "label": "a", "token": "か"},
        {"start": 0.70, "end": 0.78, "label": "s", "token": "さ"},
        {"start": 0.78, "end": 1.0, "label": "a", "token": "さ"},
    ]
    doc = {
        "mono": {
            "file": "ka.wav",
            "duration": 0.6,
            "offsetMs": 10,
            "consonantMs": 180,
            "blank": 0,
            "overlap": {"k": 0.25},
            "loudness": None,
            "spans": [
                {"start": 0.0, "end": 0.05, "label": "無音", "token": ""},
                {"start": 0.05, "end": 0.12, "label": "k", "token": "か"},
                {"start": 0.12, "end": 0.48, "label": "a", "token": "か"},
            ],
        },
        "renzoku": {
            "file": "kasa.wav",
            "duration": 1.0,
            "offsetMs": 20,
            "consonantMs": 160,
            "blank": 0.02,
            "overlapAt": 0.4,
            "loudness": loud,
            "spans": vowel,
        },
        "blank": {
            "duration": 1.0,
            "index": 1,
            "shift": 0.0,
            "loudness": loud,
            "spans": vowel,
        },
        "tunes": [
            {
                "line": "ka.wav=か,100.000,200.000,100.000,80.000,40.000",
                "alias": "か",
                "duration": 0.9,
                "points": {"overlap": 0.11},
            },
            {
                "line": "ka.wav=か,100.000,250.000,-400.000,80.000,40.000",
                "alias": "か",
                "duration": 0.9,
                "points": {"preutter": 0.2, "overlap": 0.15},
            },
        ],
    }
    doc["expected"] = compute(doc)
    CASES.write_text(json.dumps(doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


class OtoParityTest(unittest.TestCase):
    def test_recorded_python_values(self) -> None:
        doc = json.loads(CASES.read_text(encoding="utf-8"))
        self.assertEqual(compute(doc), doc["expected"])


if __name__ == "__main__":
    write_cases()
