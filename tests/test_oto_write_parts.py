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

from otoasobi.analyze import export_folder
from otoasobi.wavs import AnalyzedFile
from otoasobi.oto import (
    OtoSettings,
    leading_hyphen_alias,
    merge_existing_aliases,
    mono_oto_lines,
    renzoku_oto_lines,
)
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

    def test_renzoku_cvvc_writes_leading_hyphen_once(self) -> None:
        spans = [Span(0.0, 0.2, "a", "あ"), Span(0.2, 0.5, "k", "い")]
        item = AnalyzedFile("あい.wav", 0.8, spans)
        renzoku = OtoSettings(mode="renzoku")
        cvvc = OtoSettings(mode="cvvc")
        with tempfile.TemporaryDirectory() as tmp:
            path = export_folder(tmp, [item], parts=[(renzoku, []), (cvvc, [])])
            text = path.read_text(encoding=locale.getpreferredencoding(False))
        lines = [line for line in text.splitlines() if line]
        leading = [line for line in lines if leading_hyphen_alias(line)]
        self.assertEqual(len(leading), 1)

    def test_merge_keeps_missing_and_replaces_overlap(self) -> None:
        existing = ["a.wav=あ,1,2,3,4,5", "b.wav=か,1,2,3,4,5"]
        generated = ["a.wav=あ,9,9,9,9,9", "c.wav=さ,1,2,3,4,5"]
        self.assertEqual(
            merge_existing_aliases(existing, generated, "missing"),
            ["a.wav=あ,1,2,3,4,5", "b.wav=か,1,2,3,4,5", "c.wav=さ,1,2,3,4,5"],
        )
        self.assertEqual(
            merge_existing_aliases(existing, generated, "overlap"),
            ["a.wav=あ,9,9,9,9,9", "b.wav=か,1,2,3,4,5"],
        )
        self.assertEqual(merge_existing_aliases(existing, generated, "all"), generated)

    def test_export_missing_keeps_the_previous_line(self) -> None:
        spans = [Span(0.0, 0.2, "k", "k"), Span(0.2, 0.6, "a", "あ")]
        item = AnalyzedFile("a.wav", 0.8, spans)
        encoding = locale.getpreferredencoding(False)
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            previous = folder / "oto.ini"
            previous.write_text("a.wav=あ,1.000,2.000,3.000,4.000,5.000\r\n", encoding=encoding)
            export_folder(folder, [item], OtoSettings(mode="mono"), scope="missing")
            text = (folder / "oto.ini").read_text(encoding=encoding)
            log = (folder / "oto.log.ini").read_text(encoding=encoding)
        lines = [line for line in text.splitlines() if line]
        self.assertTrue(lines[0].startswith("a.wav=あ,1.000,"))
        self.assertEqual(log.strip(), "a.wav=あ,1.000,2.000,3.000,4.000,5.000")
        self.assertEqual(len(lines), 1)


if __name__ == "__main__":
    unittest.main()
