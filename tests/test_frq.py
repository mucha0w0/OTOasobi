"""FREQ0003 の書き出しと、正弦に対する高さ。"""

from __future__ import annotations

import struct
import sys
import tempfile
import unittest
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

import numpy as np
import soundfile as sf

from otoasobi.frq import (
    HOP,
    clear_preutterance,
    estimate_pitch,
    read_frq,
    read_mono,
    write_folder_frq,
    write_frq,
)


SR = 44100


def _sine(freq: float, seconds: float, amp: float = 0.4) -> np.ndarray:
    t = np.arange(int(SR * seconds)) / SR
    return amp * np.sin(2 * np.pi * freq * t)


def _cents(est: np.ndarray, truth: float) -> np.ndarray:
    voiced = est[est > 0]
    return 1200.0 * np.log2(voiced / truth)


class FrqTests(unittest.TestCase):
    def test_sine_near_440(self) -> None:
        y = _sine(440, 1.0)
        frequency, amplitude = estimate_pitch(y, SR)
        self.assertEqual(frequency.size, int(np.ceil(y.size / HOP)))
        self.assertEqual(frequency.shape, amplitude.shape)
        middle = frequency[8:-8]
        self.assertGreater(np.mean(middle > 0), 0.98)
        err = np.abs(_cents(middle, 440))
        self.assertLess(float(np.median(err)), 2.0)
        self.assertTrue(np.all(amplitude[8:-8][middle > 0] > 0))

    def test_silence_stays_unvoiced(self) -> None:
        y = np.zeros(int(SR * 1.2))
        a, b = int(0.4 * SR), int(0.9 * SR)
        y[a:b] = _sine(440, (b - a) / SR)
        frequency, _ = estimate_pitch(y, SR)
        hop_s = HOP / SR
        before = frequency[int(0.05 / hop_s) : int(0.25 / hop_s)]
        vowel = frequency[int(0.55 / hop_s) : int(0.75 / hop_s)]
        after = frequency[int(1.00 / hop_s) : int(1.15 / hop_s)]
        self.assertEqual(float(np.max(before)), 0.0)
        self.assertGreater(np.mean(vowel > 0), 0.95)
        self.assertLess(float(np.median(np.abs(_cents(vowel, 440)))), 2.0)
        self.assertEqual(float(np.max(after)), 0.0)

    def test_header_and_average(self) -> None:
        frequency = np.array([0.0, 100.0, 300.0, 0.0])
        amplitude = np.array([0.0, 0.2, 0.4, 0.1])
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "a.frq"
            write_frq(path, frequency, amplitude)
            data = path.read_bytes()
            self.assertEqual(data[:8], b"FREQ0003")
            self.assertEqual(len(data), 40 + frequency.size * 16)
            hop, average, count = struct.unpack_from("<id16xi", data, 8)
            self.assertEqual(hop, 256)
            self.assertEqual(count, 4)
            self.assertAlmostEqual(average, 200.0)
            self.assertEqual(data[20:36], b"\x00" * 16)
            loaded = read_frq(path)
            self.assertEqual(loaded.hop, 256)
            np.testing.assert_allclose(loaded.frequency, frequency)
            np.testing.assert_allclose(loaded.amplitude, amplitude)

    def test_short_and_stereo(self) -> None:
        short = _sine(440, 100 / SR)
        frequency, amplitude = estimate_pitch(short, SR)
        self.assertEqual(frequency.size, 1)
        self.assertEqual(amplitude.size, 1)
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "s.wav"
            sf.write(path, np.column_stack([_sine(330, 0.4), _sine(330, 0.4)]), SR)
            y, sr = read_mono(path)
        self.assertEqual(y.ndim, 1)
        self.assertEqual(sr, SR)
        frequency, _ = estimate_pitch(y, sr)
        middle = frequency[4:-4]
        self.assertGreater(np.mean(middle > 0), 0.9)
        self.assertLess(float(np.median(np.abs(_cents(middle, 330)))), 2.0)

    def test_folder_writes_root_only(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            sf.write(root / "あ.wav", _sine(440, 0.3), SR, subtype="PCM_16")
            (root / "bad.wav").write_bytes(b"not a wav")
            nested = root / "original"
            nested.mkdir()
            sf.write(nested / "あ.wav", _sine(220, 0.3), SR, subtype="PCM_16")
            batch = write_folder_frq(root)
            self.assertEqual(batch.written, 1)
            self.assertEqual(len(batch.failed), 1)
            self.assertIn("bad.wav", batch.failed[0])
            loaded = read_frq(root / "あ.frq")
            self.assertEqual(loaded.hop, 256)
            self.assertGreater(loaded.average, 400)
            self.assertLess(loaded.average, 480)
            self.assertFalse((nested / "あ.frq").exists())
            with wave.open(str(root / "あ.wav"), "rb") as wav:
                self.assertEqual(wav.getnchannels(), 1)

    def test_clear_preutterance_and_scope(self) -> None:
        frequency = np.full(10, 440.0)
        hop_s = HOP / SR
        cleared = clear_preutterance(frequency, SR, [(0.0, 3 * hop_s), (8 * hop_s, 8 * hop_s)])
        self.assertTrue(np.all(cleared[:3] == 0))
        self.assertTrue(np.all(cleared[3:8] == 440))
        self.assertTrue(np.all(cleared[8:] == 440))
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            sf.write(root / "a.wav", _sine(440, 0.25), SR, subtype="PCM_16")
            sf.write(root / "b.wav", _sine(330, 0.25), SR, subtype="PCM_16")
            first = write_folder_frq(root, mode="selected", only="a.wav")
            self.assertEqual(first.written, 1)
            self.assertTrue((root / "a.frq").is_file())
            self.assertFalse((root / "b.frq").is_file())
            second = write_folder_frq(
                root,
                mode="missing",
                clear={"b.wav": [(0.0, 0.05)]},
            )
            self.assertEqual(second.written, 1)
            self.assertTrue(any("1 件" in note for note in second.notes))
            loaded = read_frq(root / "b.frq")
            self.assertEqual(float(np.max(loaded.frequency[:8])), 0.0)
            self.assertGreater(float(np.median(loaded.frequency[12:])), 300)
            both = write_folder_frq(root, mode="selected", only=["a.wav", "gone.wav", "b.wav"])
            self.assertEqual(both.written, 2)
            self.assertTrue(any("gone.wav" in note for note in both.notes))
