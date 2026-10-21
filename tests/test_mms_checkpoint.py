"""高精度モデルを、途中までのダウンロードではなく手元のファイルから置く。"""

from __future__ import annotations

import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from otoasobi.align_mms import place_checkpoint


def _zip(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("weights", payload)


class CheckpointTests(unittest.TestCase):
    def test_reuses_local_zip_and_drops_partial(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            src = root / "cache" / "model.pt"
            _zip(src, b"weights")
            dest = root / "home" / "hub" / "checkpoints" / "model.pt"
            partial = dest.parent / "model.pt.abc.partial"
            partial.parent.mkdir(parents=True)
            partial.write_bytes(b"partial")

            self.assertTrue(place_checkpoint(dest, [src], min_bytes=1))
            self.assertTrue(zipfile.is_zipfile(dest))
            self.assertFalse(partial.exists())
            self.assertEqual(src.stat().st_ino, dest.stat().st_ino)

    def test_skips_a_truncated_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            src = root / "model.pt"
            src.write_bytes(b"not-a-zip")
            dest = root / "out" / "model.pt"

            self.assertFalse(place_checkpoint(dest, [src], min_bytes=1))
            self.assertFalse(dest.exists())

    def test_replaces_a_broken_destination(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            src = root / "cache" / "model.pt"
            _zip(src, b"ok")
            dest = root / "home" / "model.pt"
            dest.parent.mkdir()
            dest.write_bytes(b"broken")

            self.assertTrue(place_checkpoint(dest, [src], min_bytes=1))
            self.assertTrue(zipfile.is_zipfile(dest))


if __name__ == "__main__":
    unittest.main()
