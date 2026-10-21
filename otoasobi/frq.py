"""UTAU の周波数表（FREQ0003、.frq）。

リトルエンディアン。間隔は 256 サンプル。周波数 0 は無声。
平均は、有声フレームの高さ。dummy の 16 バイトは 0。
original フォルダは見ない。フォルダ直下の WAV だけを読む。
"""

from __future__ import annotations

import os
import struct
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import soundfile as sf

from otoasobi.analyze import list_wavs

HOP = 256
WIN = 2048
FMIN = 50.0
FMAX = 1100.0
THRESHOLD = 0.15
SILENCE_RMS = 1e-4
NFFT = 1 << (2 * WIN - 1).bit_length()


@dataclass(frozen=True)
class FrqFile:
    hop: int
    average: float
    frequency: np.ndarray
    amplitude: np.ndarray


def read_mono(path: str | Path) -> tuple[np.ndarray, int]:
    """ピーク正規化はしない。ステレオはチャンネルの平均。"""
    y, sr = sf.read(str(path), always_2d=False)
    y = np.asarray(y, dtype=np.float64)
    if y.ndim > 1:
        y = np.mean(y, axis=1)
    if y.ndim != 1:
        y = np.zeros(0, dtype=np.float64)
    return np.ascontiguousarray(y), int(sr)


def estimate_pitch(
    y: np.ndarray,
    sr: int,
    on_step: Callable[[float], None] | None = None,
) -> tuple[np.ndarray, np.ndarray]:
    """YIN。戻り値はフレームごとの周波数（Hz）と、窓の実効値。"""
    y = np.asarray(y, dtype=np.float64)
    if y.ndim != 1:
        y = np.reshape(y, -1)
    n = int(np.ceil(y.size / HOP)) if y.size else 0
    frequency = np.zeros(n, dtype=np.float64)
    amplitude = np.zeros(n, dtype=np.float64)
    if n == 0 or sr < 1:
        return frequency, amplitude

    need = (n - 1) * HOP + WIN
    if y.size < need:
        buf = np.zeros(need, dtype=np.float64)
        buf[: y.size] = y
        y = buf

    tau_min = max(2, int(sr / FMAX))
    tau_max = min(WIN // 2, int(sr / FMIN))
    if tau_min >= tau_max:
        for i in range(n):
            frame = y[i * HOP : i * HOP + WIN]
            amplitude[i] = float(np.sqrt(np.mean(frame * frame)))
        return frequency, amplitude

    taus = np.arange(tau_max)
    report_every = max(1, n // 40)
    for i in range(n):
        frame = y[i * HOP : i * HOP + WIN]
        amplitude[i] = float(np.sqrt(np.mean(frame * frame)))
        if amplitude[i] >= SILENCE_RMS:
            frequency[i] = _yin(frame, sr, tau_min, tau_max, taus)
        if on_step is not None and (i % report_every == 0 or i == n - 1):
            on_step((i + 1) / n)

    raw = frequency.copy()
    for i in range(n):
        lo = max(0, i - 2)
        hi = min(n, i + 3)
        frequency[i] = float(np.median(raw[lo:hi]))
    frequency[raw == 0.0] = 0.0
    return frequency, amplitude


def _yin(frame: np.ndarray, sr: int, tau_min: int, tau_max: int, taus: np.ndarray) -> float:
    spec = np.fft.rfft(frame, n=NFFT)
    acf = np.fft.irfft(spec * np.conj(spec), n=NFFT)[:tau_max]
    cs = np.cumsum(frame * frame)
    left = cs[WIN - taus - 1]
    right = np.empty(tau_max, dtype=np.float64)
    right[0] = cs[-1]
    right[1:] = cs[-1] - cs[taus[1:] - 1]
    diff = left + right - 2.0 * acf
    diff[0] = 0.0
    cmnd = np.ones(tau_max, dtype=np.float64)
    cumulative = np.cumsum(diff)
    steps = np.arange(1, tau_max)
    denom = cumulative[1:]
    good = denom > 1e-12
    cmnd[1:] = np.where(good, diff[1:] * steps / np.maximum(denom, 1e-12), 1.0)
    below = np.flatnonzero(cmnd[tau_min:tau_max] < THRESHOLD)
    if below.size == 0:
        return 0.0
    k = int(below[0]) + tau_min
    while k + 1 < tau_max and cmnd[k + 1] < cmnd[k]:
        k += 1
    if 0 < k < tau_max - 1:
        s0, s1, s2 = float(cmnd[k - 1]), float(cmnd[k]), float(cmnd[k + 1])
        den = 2.0 * s1 - s2 - s0
        shift = 0.5 * (s2 - s0) / den if den != 0.0 else 0.0
        period = k + shift
    else:
        period = float(k)
    if period <= 0.0:
        return 0.0
    return float(sr) / period


def average_frequency(frequency: np.ndarray) -> float:
    voiced = np.asarray(frequency, dtype=np.float64)
    voiced = voiced[voiced > 0.0]
    if voiced.size == 0:
        return 0.0
    return float(np.mean(voiced))


def encode_frq(frequency: np.ndarray, amplitude: np.ndarray, hop: int = HOP) -> bytes:
    frequency = np.asarray(frequency, dtype=np.float64)
    amplitude = np.asarray(amplitude, dtype=np.float64)
    if frequency.shape != amplitude.shape or frequency.ndim != 1:
        raise RuntimeError("周波数と振幅の長さが違います。")
    count = int(frequency.size)
    header = struct.pack("<8sid16si", b"FREQ0003", int(hop), average_frequency(frequency), b"\x00" * 16, count)
    pairs = np.empty(count * 2, dtype="<f8")
    pairs[0::2] = frequency
    pairs[1::2] = amplitude
    return header + pairs.tobytes()


def read_frq(path: str | Path) -> FrqFile:
    data = Path(path).read_bytes()
    if len(data) < 40 or data[:8] != b"FREQ0003":
        raise RuntimeError("frq の形式ではありません。")
    hop, average, count = struct.unpack_from("<id16xi", data, 8)
    body = data[40:]
    if count < 0 or len(body) != count * 16:
        raise RuntimeError("frq の長さが合いません。")
    if count == 0:
        empty = np.zeros(0, dtype=np.float64)
        return FrqFile(hop, average, empty, empty)
    pairs = np.frombuffer(body, dtype="<f8")
    return FrqFile(hop, average, pairs[0::2].copy(), pairs[1::2].copy())


def write_frq(path: str | Path, frequency: np.ndarray, amplitude: np.ndarray) -> None:
    path = Path(path)
    blob = encode_frq(frequency, amplitude)
    tmp = path.with_name(path.name + ".writing")
    try:
        tmp.write_bytes(blob)
        os.replace(tmp, path)
    finally:
        if tmp.exists():
            tmp.unlink()


def frq_path_for(wav: str | Path) -> Path:
    return Path(wav).with_suffix(".frq")


def clear_preutterance(
    frequency: np.ndarray,
    sr: int,
    ranges: list[tuple[float, float]],
) -> np.ndarray:
    """各範囲 [開始秒, 終了秒) に重なるフレームの周波数を 0 にする。"""
    out = np.array(frequency, dtype=np.float64, copy=True)
    if sr < 1 or out.size == 0:
        return out
    hop_s = HOP / float(sr)
    for start, end in ranges:
        if end <= start:
            continue
        first = int(np.floor(start / hop_s))
        last = int(np.ceil(end / hop_s - 1e-12))
        if first < 0:
            first = 0
        if last > out.size:
            last = int(out.size)
        if first < last:
            out[first:last] = 0.0
    return out


@dataclass(frozen=True)
class FrqBatch:
    written: int
    failed: list[str]
    notes: list[str]


def write_folder_frq(
    folder: str | Path,
    on_progress: Callable[[int, int], None] | None = None,
    on_file: Callable[[str, float, str], None] | None = None,
    mode: str = "all",
    only: str | None = None,
    clear: dict[str, list[tuple[float, float]]] | None = None,
) -> FrqBatch:
    """直下の WAV に .frq を書く。mode は all、missing、selected。"""
    folder = Path(folder)
    if not folder.is_dir():
        raise RuntimeError("フォルダが見つかりません。")
    wavs = list_wavs(folder)
    if not wavs:
        raise RuntimeError("WAV ファイルがありません。")
    targets, notes = _frq_targets(wavs, mode, only)
    if not targets:
        if mode == "missing":
            return FrqBatch(0, [], notes or ["新しく書くファイルはありません。"])
        raise RuntimeError("WAV ファイルがありません。")
    written = 0
    failed: list[str] = []
    uncleared: list[str] = []
    total = len(targets)
    for index, wav in enumerate(targets):
        if on_progress is not None:
            on_progress(index, total)
        try:
            if on_file is not None:
                on_file(wav.name, 0.0, "読み込んでいます")
            y, sr = read_mono(wav)

            def step(fraction: float, name: str = wav.name) -> None:
                if on_file is not None:
                    on_file(name, 0.05 + 0.9 * fraction, "周波数を計算しています")

            frequency, amplitude = estimate_pitch(y, sr, step)
            if clear is not None:
                ranges = clear.get(wav.name.casefold())
                if ranges is None:
                    uncleared.append(wav.name)
                elif ranges:
                    frequency = clear_preutterance(frequency, sr, ranges)
            if on_file is not None:
                on_file(wav.name, 0.96, "書き込んでいます")
            write_frq(frq_path_for(wav), frequency, amplitude)
            written += 1
            if on_file is not None:
                on_file(wav.name, 1.0, "書きました")
        except Exception as exc:
            failed.append(f"{wav.name}（{exc}）")
    if on_progress is not None:
        on_progress(total, total)
    if uncleared:
        names = "\n".join(uncleared)
        notes.append(f"原音設定がないので、先行発声までは 0 にしていません。\n{names}")
    return FrqBatch(written, failed, notes)


def _frq_targets(wavs: list[Path], mode: str, only: str | None) -> tuple[list[Path], list[str]]:
    if mode == "selected":
        if not isinstance(only, str) or not only or Path(only).name != only:
            raise RuntimeError("ファイルを選んでください。")
        match = [wav for wav in wavs if wav.name.casefold() == only.casefold()]
        if len(match) != 1:
            raise RuntimeError("選んだ音声が見つかりません。")
        return match, []
    if mode == "missing":
        targets: list[Path] = []
        skipped = 0
        for wav in wavs:
            if frq_path_for(wav).is_file():
                skipped += 1
            else:
                targets.append(wav)
        notes = [f"すでにある .frq {skipped} 件は飛ばしました。"] if skipped else []
        return targets, notes
    if mode != "all":
        raise RuntimeError("生成の対象が不正です。")
    return list(wavs), []
