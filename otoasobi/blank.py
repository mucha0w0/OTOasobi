"""ブランクを、音量の上辺の右端から秒数でずらす。

0 秒がその角。正は減衰側、負は手前。
右脚が取れないときは、母音の終了から同じ秒数だけずらす。

画面の winui/BlankRelease.cs と揃える。
"""

from __future__ import annotations

import numpy as np

from otoasobi.phones import BREATH_LABEL, SILENCE_LABEL
from otoasobi.segment import Span

# 上辺の高さから、立ち上がりを外す長さ。
ATTACK_SKIP_S = 0.050
# 0% を決める前に、表示用の 30 ms 平滑へさらにかける長さ。
SMOOTH_S = 0.080
# これより短い母音は、右脚を取らない。
MIN_WINDOW_S = 0.080
# 有音とみなす、底からの高さ。
BODY_MARGIN_DB = 12.0
# 上辺とみなす幅。ビブラートがこれより広いときは、揺れの幅まで広げる。
BASE_TOLERANCE_DB = 1.5
MAX_TOLERANCE_DB = 6.0
# この深さまで落ちたら、そのあとの持ち上がりは上辺に含めない。
LATE_GAP_DB = 8.0
# 100% は、上辺からここまで落ちた点。
DROP_DB = 12.0


def release_window_end(spans: list[Span], index: int, duration: float) -> float:
    """母音の終了。直後が無音や息なら、その終わりまで延ばす。"""
    end = float(spans[index].end)
    i = index + 1
    while i < len(spans) and spans[i].label in (SILENCE_LABEL, BREATH_LABEL):
        end = max(end, float(spans[i].end))
        i += 1
    return min(float(duration), end)


def release_blank_seconds(
    loudness_db: np.ndarray | None,
    duration: float,
    spans: list[Span],
    index: int,
    shift: float,
) -> float:
    """ファイル先頭からの秒。右脚が取れないときは母音の終了から同じ秒数。"""
    vowel_end = float(spans[index].end)
    shift = float(shift)
    if (
        loudness_db is None
        or duration <= 0
        or vowel_end - float(spans[index].start) < MIN_WINDOW_S
    ):
        return vowel_end + shift
    light = np.asarray(loudness_db, dtype=np.float64).reshape(-1)
    count = int(light.shape[0])
    if count < 2:
        return vowel_end + shift
    times = (np.arange(count, dtype=np.float64) + 0.5) * float(duration) / float(count)
    found = _anchors(times, light, float(spans[index].start), release_window_end(spans, index, duration))
    if found is None:
        return vowel_end + shift
    t0, _t1 = found
    return t0 + shift


def _percentile(values: np.ndarray, q: float) -> float:
    return float(np.percentile(values, q, method="linear"))


def _smooth(values: np.ndarray, hop_s: float) -> np.ndarray:
    win = int(round(SMOOTH_S / hop_s)) if hop_s > 0 else 1
    if win % 2 == 0:
        win += 1
    if win <= 1 or len(values) < win:
        return np.array(values, dtype=np.float64, copy=True)
    pad = win // 2
    kernel = np.ones(win, dtype=np.float64) / float(win)
    padded = np.pad(values, (pad, pad), mode="edge")
    return np.convolve(padded, kernel, mode="valid")


def _anchors(
    times: np.ndarray,
    light: np.ndarray,
    t_start: float,
    t_end: float,
) -> tuple[float, float] | None:
    if t_end - t_start < MIN_WINDOW_S or len(times) < 2:
        return None
    hop = float(times[1] - times[0])
    heavy = _smooth(light, hop)
    level_from = t_start + ATTACK_SKIP_S
    if t_end - level_from < MIN_WINDOW_S:
        level_from = t_start

    window_h: list[float] = []
    scan_t: list[float] = []
    scan_h: list[float] = []
    scan_l: list[float] = []
    for index in range(len(times)):
        time = float(times[index])
        if time < t_start - 1e-6 or time > t_end + 1e-6:
            continue
        level = float(heavy[index])
        window_h.append(level)
        if time < level_from - 1e-6:
            continue
        scan_t.append(time)
        scan_h.append(level)
        scan_l.append(float(light[index]))
    if len(window_h) < 4 or len(scan_h) < 3:
        return None

    floor = _percentile(np.asarray(window_h), 10.0)
    above = [value for value in scan_h if value > floor + BODY_MARGIN_DB]
    top_src = np.asarray(above if len(above) >= 3 else scan_h, dtype=np.float64)
    top = _percentile(top_src, 75.0)

    third = len(scan_l) // 3
    if len(scan_l) - third - third >= 3:
        residual = np.asarray(scan_l[third : len(scan_l) - third], dtype=np.float64) - np.asarray(
            scan_h[third : len(scan_l) - third], dtype=np.float64
        )
        width = _percentile(residual, 90.0) - _percentile(residual, 10.0)
    else:
        width = 0.0
    tolerance = min(MAX_TOLERANCE_DB, max(BASE_TOLERANCE_DB, width))
    threshold = top - tolerance
    deep = top - LATE_GAP_DB

    entered = False
    t0: float | None = None
    for time, value in zip(scan_t, scan_h, strict=True):
        if entered and value <= deep:
            break
        if value >= threshold:
            entered = True
            t0 = time
    if t0 is None:
        return None

    t1 = float(t_end)
    drop = top - DROP_DB
    for time, value in zip(scan_t, scan_h, strict=True):
        if time + 1e-9 < t0:
            continue
        if value <= drop:
            t1 = time
            break
    if t1 < t0:
        t1 = t0
    return t0, t1
