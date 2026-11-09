"""フォルダ解析は、ファイルのあいだで止まる。"""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from otoasobi.analyze import AnalysisCancelled, analyze_folder_files  # noqa: E402


class AnalyzeCancelTests(unittest.TestCase):
    def test_stops_before_the_first_file(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            Path(folder, "a.wav").write_bytes(b"")
            with self.assertRaises(AnalysisCancelled):
                analyze_folder_files(folder, stop=lambda: True)


if __name__ == "__main__":
    unittest.main()
