"""常駐の解析プロセス。WinUI アプリが標準入出力の JSON で呼び出す。

1行に1オブジェクト。
要求: {"id": 1, "cmd": "analyze", "path": "C:/音声フォルダ"}
途中: {"id": 1, "event": "progress", "done": 1, "total": 4}
      {"id": 1, "event": "file", "name": "あ.wav", "done": 0.4, "label": "特徴を計算しています"}
file の done は、いま解析しているファイルの 0 から 1。
応答: {"id": 1, "ok": true, "files": [...], "notice": "..."}
      {"id": 1, "ok": false, "error": "..."}

analyze は oto.ini を書かない。区間は OTOasibi_cache.json に残す。
同じフォルダに保存データがあるときは再解析せず、そのファイルを返す。
"force": true のときだけ解析し直し、保存データを上書きする。
要求: {"id": 2, "cmd": "preview", "path": "C:/音声フォルダ/あ.wav", "t0": 0, "t1": 1.2, "columns": 1600, "rows": 400}
preview の t0/t1 は表示中の秒。columns/rows は画面のピクセル数。
波形はその幅の最小最大で、サンプルの方が少なければ 1 点ずつ返す。
スペクトラムは同じ範囲をそのピクセル数へ最近傍で描き、拡大しても引き伸ばさない。
要求: {"id": 3, "cmd": "save", "folder": "C:/音声フォルダ", "files": [...]}
要求: {"id": 4, "cmd": "write", "folder": "C:/音声フォルダ", "files": [...], "config": {...}}
config は offset_ms, consonant_ms（ミリ秒）、blank（秒、-1 から 1）と overlap（0 から 1）。無いときは初期値。
blank の 0 は音量の上辺の右端。正は減衰側、負は手前へ、その秒数だけずらす。
応答の txt は単独音モードの oto.ini。全区間は OTOasibi_log.txt、母音始まりは OTOasibi_out.txt。
画面で区間を直したあとは save が保存データを更新し、write が oto.ini を計算して書く。
"""

from __future__ import annotations

import base64
import json
import os
import sys
import traceback

from otoasobi.analyze import (
    AnalyzedFile,
    analyze_folder_files,
    export_folder,
    load_wav,
    skipped_notice,
)
from otoasobi.cache import file_from_mapping, file_to_mapping, read_cache, save_cache
from otoasobi.oto import oto_settings_from
from otoasobi.parser import FilenameError
from otoasobi.preview import render_preview


def _respond(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def _analyze(request_id: object, path: str, force: bool) -> dict:
    if not force:
        loaded = read_cache(path)
        if loaded is not None:
            payload: dict = {
                "id": request_id,
                "ok": True,
                "cached": True,
                "files": [file_to_mapping(item) for item in loaded.files],
            }
            if loaded.notice:
                payload["notice"] = loaded.notice
            return payload

    def on_progress(done: int, total: int) -> None:
        _respond({"id": request_id, "event": "progress", "done": done, "total": total})

    shown = {"name": "", "done": -1.0, "label": ""}

    def on_file(name: str, fraction: float, label: str) -> None:
        done = round(max(0.0, min(1.0, float(fraction))), 3)
        if (
            shown["name"] == name
            and shown["label"] == label
            and done < 1.0
            and done - float(shown["done"]) < 0.02
        ):
            return
        shown["name"] = name
        shown["done"] = done
        shown["label"] = label
        _respond(
            {
                "id": request_id,
                "event": "file",
                "name": name,
                "done": done,
                "label": label,
            }
        )

    files, skipped = analyze_folder_files(path, on_progress=on_progress, on_file=on_file)
    if not files:
        return {"id": request_id, "ok": False, "error": skipped_notice(skipped)}
    notes: list[str] = []
    if skipped:
        notes.append(skipped_notice(skipped))
    try:
        save_cache(path, files)
    except Exception as exc:
        notes.append(f"保存データを書けませんでした。{exc}")
    payload = {
        "id": request_id,
        "ok": True,
        "files": [file_to_mapping(item) for item in files],
    }
    if notes:
        payload["notice"] = "\n".join(notes)
    return payload


_wav_cache: tuple[str, int, int, object, int] | None = None


def _load_cached(path: str) -> tuple[np.ndarray, int]:
    global _wav_cache
    st = os.stat(path)
    key = (path, st.st_mtime_ns, st.st_size)
    if _wav_cache is not None and _wav_cache[0] == key[0] and _wav_cache[1] == key[1] and _wav_cache[2] == key[2]:
        return _wav_cache[3], _wav_cache[4]
    y, sr = load_wav(path)
    _wav_cache = (key[0], key[1], key[2], y, sr)
    return y, sr


def _optional_float(value: object) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return float(value)


def _optional_int(value: object, default: int, lo: int, hi: int) -> int:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return default
    return max(lo, min(hi, int(value)))


def _preview(path: str, request: dict) -> dict:
    y, sr = _load_cached(path)
    duration = len(y) / float(sr) if sr else 0.0
    t0 = _optional_float(request.get("t0"))
    t1 = _optional_float(request.get("t1"))
    if t0 is None:
        t0 = 0.0
    if t1 is None or t1 <= t0:
        t1 = duration
    columns = _optional_int(request.get("columns"), 1600, 32, 4096)
    rows = _optional_int(request.get("rows"), 320, 32, 1200)
    preview = render_preview(y, sr, t0, t1, columns, rows)
    payload = {
        "ok": True,
        "wave_lo": preview.wave_lo,
        "wave_hi": preview.wave_hi,
        "wave_t0": preview.wave_t0,
        "wave_t1": preview.wave_t1,
        "wave_samples": preview.samples,
        "sr": preview.sr,
        "spec_png": base64.b64encode(preview.png).decode("ascii"),
        "spec_t0": preview.spec_t0,
        "spec_t1": preview.spec_t1,
    }
    if preview.loudness:
        payload["loudness"] = preview.loudness
    return payload


def _parsed_files(files: object) -> list[AnalyzedFile]:
    if not isinstance(files, list) or not files:
        raise RuntimeError("修正データがありません。")
    return [file_from_mapping(item) for item in files]


def _save(folder: str, files: object) -> dict:
    parsed = _parsed_files(files)
    save_cache(folder, parsed)
    return {"ok": True}


def _write(folder: str, files: object, config: object) -> dict:
    parsed = _parsed_files(files)
    out = export_folder(folder, parsed, oto_settings_from(config))
    try:
        save_cache(folder, parsed)
    except Exception as exc:
        return {
            "ok": True,
            "txt": str(out),
            "notice": f"oto.ini は作成しましたが、保存データを更新できませんでした。{exc}",
        }
    return {"ok": True, "txt": str(out)}


def main() -> None:
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
        except json.JSONDecodeError as exc:
            _respond({"id": None, "ok": False, "error": f"不正な要求です: {exc}"})
            continue
        request_id = request.get("id")
        try:
            command = request.get("cmd")
            if command == "analyze":
                path = request.get("path")
                if not isinstance(path, str) or not path:
                    raise RuntimeError("path がありません")
                payload = _analyze(request_id, path, request.get("force") is True)
            elif command == "preview":
                path = request.get("path")
                if not isinstance(path, str) or not path:
                    raise RuntimeError("path がありません")
                payload = _preview(path, request)
            elif command == "save":
                folder = request.get("folder")
                if not isinstance(folder, str) or not folder:
                    raise RuntimeError("folder がありません")
                payload = _save(folder, request.get("files"))
            elif command == "write":
                folder = request.get("folder")
                if not isinstance(folder, str) or not folder:
                    raise RuntimeError("folder がありません")
                payload = _write(folder, request.get("files"), request.get("config"))
            else:
                raise RuntimeError("未対応のコマンドです")
        except FilenameError as exc:
            _respond({"id": request_id, "ok": False, "error": str(exc)})
        except Exception as exc:
            traceback.print_exc()
            _respond({"id": request_id, "ok": False, "error": str(exc)})
        else:
            payload["id"] = request_id
            _respond(payload)


if __name__ == "__main__":
    main()
