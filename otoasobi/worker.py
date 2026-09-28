"""常駐の解析プロセス。WinUI アプリが標準入出力の JSON で呼び出す。

1行に1オブジェクト。
要求: {"id": 1, "cmd": "analyze", "path": "C:/音声フォルダ"}
途中: {"id": 1, "event": "progress", "done": 1, "total": 4}
応答: {"id": 1, "ok": true, "files": [...], "notice": "..."}
      {"id": 1, "ok": false, "error": "..."}

analyze は oto.ini を書かない。区間は OTOasibi_cache.json に残す。
同じフォルダに保存データがあるときは再解析せず、そのファイルを返す。
"force": true のときだけ解析し直し、保存データを上書きする。
要求: {"id": 2, "cmd": "preview", "path": "C:/音声フォルダ/あ.wav"}
要求: {"id": 3, "cmd": "save", "folder": "C:/音声フォルダ", "files": [...]}
要求: {"id": 4, "cmd": "write", "folder": "C:/音声フォルダ", "files": [...]}
応答の txt は単独音モードの oto.ini。全区間は OTOasibi_log.txt、母音始まりは OTOasibi_out.txt。
画面で区間を直したあとは save が保存データを更新し、write が oto.ini を計算して書く。
"""

from __future__ import annotations

import base64
import json
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

    files, skipped = analyze_folder_files(path, on_progress=on_progress)
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


def _preview(path: str) -> dict:
    y, sr = load_wav(path)
    preview = render_preview(y, sr)
    return {
        "ok": True,
        "wave_lo": preview.wave_lo,
        "wave_hi": preview.wave_hi,
        "loudness": preview.loudness,
        "spec_png": base64.b64encode(preview.png).decode("ascii"),
        "spec_t0": preview.spec_t0,
        "spec_t1": preview.spec_t1,
    }


def _parsed_files(files: object) -> list[AnalyzedFile]:
    if not isinstance(files, list) or not files:
        raise RuntimeError("修正データがありません。")
    return [file_from_mapping(item) for item in files]


def _save(folder: str, files: object) -> dict:
    parsed = _parsed_files(files)
    save_cache(folder, parsed)
    return {"ok": True}


def _write(folder: str, files: object) -> dict:
    parsed = _parsed_files(files)
    out = export_folder(folder, parsed)
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
                payload = _preview(path)
            elif command == "save":
                folder = request.get("folder")
                if not isinstance(folder, str) or not folder:
                    raise RuntimeError("folder がありません")
                payload = _save(folder, request.get("files"))
            elif command == "write":
                folder = request.get("folder")
                if not isinstance(folder, str) or not folder:
                    raise RuntimeError("folder がありません")
                payload = _write(folder, request.get("files"))
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
