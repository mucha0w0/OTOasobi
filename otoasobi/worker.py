"""常駐の解析プロセス。WinUI アプリが標準入出力の JSON で呼び出す。

1行に1オブジェクト。
要求: {"id": 1, "cmd": "analyze", "path": "C:/音声フォルダ"}
途中: {"id": 1, "event": "progress", "done": 1, "total": 4}
応答: {"id": 1, "ok": true, "files": [...], "notice": "..."}
      {"id": 1, "ok": false, "error": "..."}

analyze は oto.ini を書かない。画面で区間を直したあと、write が計算して書く。
要求: {"id": 2, "cmd": "preview", "path": "C:/音声フォルダ/あ.wav"}
要求: {"id": 3, "cmd": "write", "folder": "C:/音声フォルダ", "files": [...]}
応答の txt は単独音モードの oto.ini。全区間は OTOasibi_log.txt、母音始まりは OTOasibi_out.txt。
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
from otoasobi.parser import FilenameError
from otoasobi.preview import render_preview
from otoasobi.segment import Span


def _respond(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def _file_payload(item: AnalyzedFile) -> dict:
    return {
        "name": item.name,
        "duration": item.duration,
        "engine": item.engine,
        "spans": [
            {"start": span.start, "end": span.end, "label": span.label, "token": span.token}
            for span in item.spans
        ],
    }


def _analyze(request_id: object, path: str) -> dict:
    def on_progress(done: int, total: int) -> None:
        _respond({"id": request_id, "event": "progress", "done": done, "total": total})

    files, skipped = analyze_folder_files(path, on_progress=on_progress)
    if not files:
        return {"id": request_id, "ok": False, "error": skipped_notice(skipped)}
    payload: dict = {
        "id": request_id,
        "ok": True,
        "files": [_file_payload(item) for item in files],
    }
    if skipped:
        payload["notice"] = skipped_notice(skipped)
    return payload


def _preview(path: str) -> dict:
    y, sr = load_wav(path)
    preview = render_preview(y, sr)
    return {
        "ok": True,
        "wave_lo": preview.wave_lo,
        "wave_hi": preview.wave_hi,
        "spec_png": base64.b64encode(preview.png).decode("ascii"),
        "spec_t0": preview.spec_t0,
        "spec_t1": preview.spec_t1,
    }


def _span_from_json(row: object, duration: float) -> Span:
    if not isinstance(row, dict):
        raise RuntimeError("区間の形式が違います。")
    try:
        start = float(row["start"])
        end = float(row["end"])
    except (KeyError, TypeError, ValueError) as exc:
        raise RuntimeError("区間の時刻が不正です。") from exc
    label = row.get("label")
    token = row.get("token", "")
    if not isinstance(label, str) or not isinstance(token, str):
        raise RuntimeError("区間のラベルが不正です。")
    if duration > 0:
        start = min(max(0.0, start), duration)
        end = min(max(0.0, end), duration)
    if end < start:
        start, end = end, start
    if end <= start:
        end = min(duration, start + 0.001) if duration > 0 else start + 0.001
        if end <= start:
            start = max(0.0, end - 0.001)
    return Span(start=start, end=end, label=label, token=token)


def _file_from_json(item: object) -> AnalyzedFile:
    if not isinstance(item, dict):
        raise RuntimeError("修正データの形式が違います。")
    name = item.get("name")
    if not isinstance(name, str) or not name or "/" in name or "\\" in name or name.startswith("."):
        raise RuntimeError("ファイル名が不正です。")
    try:
        duration = float(item.get("duration", 0))
    except (TypeError, ValueError) as exc:
        raise RuntimeError(f"{name} の長さが不正です。") from exc
    raw = item.get("spans")
    if not isinstance(raw, list) or not raw:
        raise RuntimeError(f"{name} の区間がありません。")
    if duration < 0:
        duration = 0.0
    spans = [_span_from_json(row, duration) for row in raw]
    if duration <= 0:
        duration = max(span.end for span in spans)
    return AnalyzedFile(name=name, duration=duration, spans=spans)


def _write(folder: str, files: object) -> dict:
    if not isinstance(files, list) or not files:
        raise RuntimeError("修正データがありません。")
    parsed = [_file_from_json(item) for item in files]
    out = export_folder(folder, parsed)
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
                payload = _analyze(request_id, path)
            elif command == "preview":
                path = request.get("path")
                if not isinstance(path, str) or not path:
                    raise RuntimeError("path がありません")
                payload = _preview(path)
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
