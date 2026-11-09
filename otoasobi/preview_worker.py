"""プレビューだけを話す常駐プロセス。解析、oto、frq は読み込まない。

ホストは `python -m otoasobi.preview_worker` で起動する。
要求は worker の preview と同じで、溜まった preview は最新だけ計算する。
"""

from __future__ import annotations

import base64
import json
import os
import sys
import threading
import traceback
from collections import OrderedDict, deque
from collections.abc import Callable

from otoasobi.preview import FilmSkipped, film_ready, index_wave, render_index, render_preview, spectrogram_only
from otoasobi.wavload import load_wav

_WAV_KEEP = 12
_wavs: OrderedDict[tuple[str, int, int], tuple] = OrderedDict()


def _load_cached(path: str):
    st = os.stat(path)
    key = (path, st.st_mtime_ns, st.st_size)
    cached = _wavs.get(key)
    if cached is not None:
        _wavs.move_to_end(key)
        return cached[0], cached[1], key
    y, sr = load_wav(path)
    _wavs[key] = (y, sr)
    while len(_wavs) > _WAV_KEEP:
        _wavs.popitem(last=False)
    return y, sr, key


def _optional_float(value: object) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return float(value)


def _optional_int(value: object, default: int, lo: int, hi: int) -> int:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return default
    return max(lo, min(hi, int(value)))


def _wave_fields(preview: object, duration: float) -> dict:
    payload = {
        "ok": True,
        "duration": duration,
        "wave_lo": preview.wave_lo,
        "wave_hi": preview.wave_hi,
        "wave_t0": preview.wave_t0,
        "wave_t1": preview.wave_t1,
        "wave_samples": preview.samples,
        "sr": preview.sr,
    }
    if preview.loudness:
        payload["loudness"] = preview.loudness
    return payload


def preview_payload(
    path: str,
    request: dict,
    request_id: object,
    respond: Callable[[dict], None],
    waiting: Callable[[], bool],
) -> dict:
    """1 件の preview を計算する。waiting が真なら、計算の途中で skipped にする。"""
    y, sr, key = _load_cached(path)
    duration = len(y) / float(sr) if sr else 0.0
    t0 = _optional_float(request.get("t0"))
    t1 = _optional_float(request.get("t1"))
    if t0 is None:
        t0 = 0.0
    if t1 is None or t1 <= t0:
        t1 = duration
    columns = _optional_int(request.get("columns"), 1600, 32, 4096)
    rows = _optional_int(request.get("rows"), 320, 32, 1200)
    want_wave = request.get("wave") is not False
    try:
        if request.get("spec") == "index":
            if want_wave and not film_ready(y, sr, key):
                early = index_wave(y, sr, t0, t1, columns)
                respond({"id": request_id, "event": "wave", **_wave_fields(early, duration)})
            preview = (
                render_index(y, sr, t0, t1, columns, waiting, key)
                if want_wave
                else spectrogram_only(y, sr, t0, t1, columns, waiting, key)
            )
            payload = _wave_fields(preview, duration)
            payload["spec_index"] = base64.b64encode(preview.spec).decode("ascii")
            payload["spec_columns"] = preview.spec_columns
            payload["spec_rows"] = preview.spec_rows
            payload["spec_t0"] = preview.spec_t0
            payload["spec_t1"] = preview.spec_t1
            return payload
        preview = render_preview(y, sr, t0, t1, columns, rows)
    except FilmSkipped:
        return {"ok": True, "skipped": True}
    payload = _wave_fields(preview, duration)
    payload["spec_png"] = base64.b64encode(preview.png).decode("ascii")
    payload["spec_t0"] = preview.spec_t0
    payload["spec_t1"] = preview.spec_t1
    return payload


def _respond(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


_lines: deque[str] = deque()
_lines_cv = threading.Condition()
_stdin_closed = False


def _read_stdin() -> None:
    global _stdin_closed
    for line in sys.stdin:
        with _lines_cv:
            _lines.append(line)
            _lines_cv.notify()
    with _lines_cv:
        _stdin_closed = True
        _lines_cv.notify()


def _pull_line(block: bool) -> str | None:
    with _lines_cv:
        while not _lines and not _stdin_closed:
            if not block:
                return None
            _lines_cv.wait()
        if not _lines:
            return None
        return _lines.popleft()


def _parse_request(line: str) -> dict | None:
    line = line.strip()
    if not line:
        return None
    try:
        request = json.loads(line)
    except json.JSONDecodeError as exc:
        _respond({"id": None, "ok": False, "error": f"不正な要求です: {exc}"})
        return None
    if not isinstance(request, dict):
        _respond({"id": None, "ok": False, "error": "不正な要求です"})
        return None
    return request


def _waiting() -> bool:
    with _lines_cv:
        for line in _lines:
            try:
                request = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(request, dict) and request.get("cmd") == "preview":
                return True
    return False


def _coalesce(request: dict) -> dict:
    if request.get("cmd") != "preview":
        return request
    while True:
        line = _pull_line(False)
        if line is None:
            return request
        nxt = _parse_request(line)
        if nxt is None:
            continue
        if nxt.get("cmd") != "preview":
            with _lines_cv:
                _lines.appendleft(line)
            return request
        _respond({"id": request.get("id"), "ok": True, "skipped": True})
        request = nxt


def _dispatch(request: dict) -> None:
    request_id = request.get("id")
    try:
        if request.get("cmd") != "preview":
            raise RuntimeError("未対応のコマンドです")
        path = request.get("path")
        if not isinstance(path, str) or not path:
            raise RuntimeError("path がありません")
        payload = preview_payload(path, request, request_id, _respond, _waiting)
    except Exception as exc:
        traceback.print_exc()
        _respond({"id": request_id, "ok": False, "error": str(exc)})
        return
    payload["id"] = request_id
    _respond(payload)


def main() -> None:
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    threading.Thread(target=_read_stdin, name="otoasobi-preview-stdin", daemon=True).start()
    while True:
        line = _pull_line(True)
        if line is None:
            break
        request = _parse_request(line)
        if request is None:
            continue
        _dispatch(_coalesce(request))


if __name__ == "__main__":
    main()
