import json
import tempfile
import unittest
from pathlib import Path

from otoasobi.cache import CACHE_NAME, merge_cache, save_cache
from otoasobi.segment import Span
from otoasobi.wavs import AnalyzedFile


def _file(name: str, start: float, end: float) -> AnalyzedFile:
    return AnalyzedFile(
        name=name,
        duration=end,
        spans=[Span(start=start, end=end, label="a", token="a")],
        engine="acoustic",
    )


class CacheMergeTest(unittest.TestCase):
    def test_merge_keeps_other_files(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            save_cache(folder, [_file("あ.wav", 0.0, 1.0), _file("い.wav", 0.0, 1.0)])
            merge_cache(folder, [_file("あ.wav", 0.1, 0.9)])
            raw = json.loads((folder / CACHE_NAME).read_text(encoding="utf-8"))
            by_name = {item["name"]: item for item in raw["files"]}
            self.assertEqual(len(by_name), 2)
            self.assertAlmostEqual(by_name["あ.wav"]["spans"][0]["start"], 0.1)
            self.assertAlmostEqual(by_name["い.wav"]["spans"][0]["start"], 0.0)

    def test_merge_appends_new(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            save_cache(folder, [_file("あ.wav", 0.0, 1.0)])
            merge_cache(folder, [_file("い.wav", 0.0, 1.0)])
            raw = json.loads((folder / CACHE_NAME).read_text(encoding="utf-8"))
            names = [item["name"] for item in raw["files"]]
            self.assertEqual(names, ["あ.wav", "い.wav"])


if __name__ == "__main__":
    unittest.main()
