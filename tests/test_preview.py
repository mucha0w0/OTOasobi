"""配布画面の波形と、ファイル単位のメル。"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from otoasobi import preview as preview_module  # noqa: E402
from otoasobi.preview import (  # noqa: E402
    INDEX_COLUMNS,
    FilmSkipped,
    _LUT,
    film_for,
    film_ready,
    index_wave,
    render_index,
    render_preview,
    waveform_view,
)


class PreviewTests(unittest.TestCase):
    def test_waveform_bins_match_minmax(self) -> None:
        y = np.linspace(-1.0, 1.0, 12)
        lo, hi, t0, t1, samples = waveform_view(y, 12, 0.0, 1.0, 4)
        self.assertFalse(samples)
        self.assertEqual(t0, 0.0)
        self.assertEqual(t1, 1.0)
        self.assertEqual(len(lo), 4)
        for index in range(4):
            chunk = y[index * 3 : (index + 1) * 3]
            self.assertAlmostEqual(float(lo[index]), float(chunk.min()))
            self.assertAlmostEqual(float(hi[index]), float(chunk.max()))

    def test_short_waveform_keeps_samples(self) -> None:
        y = np.array([0.2, -0.5, 0.1])
        lo, hi, _t0, _t1, samples = waveform_view(y, 3, 0.0, 1.0, 8)
        self.assertTrue(samples)
        self.assertEqual(lo.tolist(), y.tolist())
        self.assertEqual(hi.tolist(), y.tolist())

    def test_index_slices_without_recomputing(self) -> None:
        sr = 16000
        y = np.sin(2 * np.pi * 220 * np.arange(sr) / sr).astype(np.float64)
        self.assertFalse(film_ready(y, sr))
        early = index_wave(y, sr, 0.0, 1.0, 4000)
        self.assertFalse(film_ready(y, sr))
        self.assertLessEqual(len(early.wave_lo), INDEX_COLUMNS)
        first = render_index(y, sr, 0.0, 1.0, 4000)
        self.assertTrue(film_ready(y, sr))
        self.assertLessEqual(first.spec_columns, INDEX_COLUMNS)
        self.assertGreater(first.spec_rows, 16)
        self.assertEqual(len(first.spec), first.spec_columns * first.spec_rows)
        zoomed = render_index(y, sr, 0.25, 0.5, 80)
        self.assertLessEqual(zoomed.spec_columns, 80)
        self.assertEqual(zoomed.spec_rows, first.spec_rows)
        self.assertTrue(zoomed.spec_t0 < zoomed.spec_t1)

    def test_png_preview_still_encodes(self) -> None:
        y = np.zeros(1600, dtype=np.float64)
        preview = render_preview(y, 16000, 0.0, 0.1, 32, 32)
        self.assertTrue(preview.png.startswith(b"\x89PNG"))

    def test_cancelled_film_is_not_cached(self) -> None:
        preview_module._film = None
        y = np.zeros(16000 * 2, dtype=np.float64)
        with self.assertRaises(FilmSkipped):
            film_for(y, 16000, lambda: True)
        self.assertFalse(film_ready(y, 16000))

    def test_lut_ends(self) -> None:
        self.assertEqual(_LUT[0].tolist(), [0, 0, 4])
        self.assertEqual(_LUT[255].tolist(), [252, 253, 191])


if __name__ == "__main__":
    unittest.main()
