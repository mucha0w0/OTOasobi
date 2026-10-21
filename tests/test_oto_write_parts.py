"""選んだエイリアスを、1 つの oto.ini に順へ足す。"""

from __future__ import annotations

import locale
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from otoasobi.analyze import AnalyzedFile, export_folder
from otoasobi.oto import OtoSettings, mono_oto_lines, renzoku_oto_lines
from otoasobi.segment import Span


class WritePartsTests(unittest.TestCase):
    def test_parts_append_per_file(self) -> None:
        spans = [Span(0.0, 0.2, "k", "k"), Span(0.2, 0.6, "a", "あ")]
        item = AnalyzedFile("a.wav", 0.8, spans)
        mono = OtoSettings(mode="mono")
        renzoku = OtoSettings(mode="renzoku")
        expected = mono_oto_lines("a.wav", spans, 0.8, mono) + renzoku_oto_lines(
            "a.wav", spans, 0.8, renzoku
        )
        with tempfile.TemporaryDirectory() as tmp:
            path = export_folder(tmp, [item], parts=[(mono, []), (renzoku, [])])
            text = path.read_text(encoding=locale.getpreferredencoding(False))
        self.assertEqual([line for line in text.splitlines() if line], expected)


if __name__ == "__main__":
    unittest.main()
