"""常駐の解析プロセス。WinUI アプリが標準入出力の JSON で呼び出す。

1行に1オブジェクト。
要求: {"id": 1, "cmd": "analyze", "path": "C:/音声フォルダ"}
途中: {"id": 1, "event": "progress", "done": 1, "total": 4}
応答: {"id": 1, "ok": true, "txt": "C:/音声フォルダ/oto.ini", "notice": "..."}
      {"id": 1, "ok": false, "error": "..."}
応答の txt は単独音モードの oto.ini。全区間は OTOasibi_log.txt、母音始まりは OTOasibi_out.txt。
"""

from __future__ import annotations

import json
import sys
import traceback

from otoasobi.analyze import analyze_folder, skipped_notice
from otoasobi.parser import FilenameError


def _respond(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def _analyze(request_id: object, path: str) -> dict:
    def on_progress(done: int, total: int) -> None:
        _respond({"id": request_id, "event": "progress", "done": done, "total": total})

    result = analyze_folder(path, on_progress=on_progress)
    if result.txt is None:
        return {"id": request_id, "ok": False, "error": skipped_notice(result.skipped)}
    payload: dict = {"id": request_id, "ok": True, "txt": str(result.txt)}
    if result.skipped:
        payload["notice"] = skipped_notice(result.skipped)
    return payload


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
            if request.get("cmd") != "analyze":
                raise RuntimeError("未対応のコマンドです")
            path = request.get("path")
            if not isinstance(path, str) or not path:
                raise RuntimeError("path がありません")
            payload = _analyze(request_id, path)
        except FilenameError as exc:
            _respond({"id": request_id, "ok": False, "error": str(exc)})
        except Exception as exc:
            traceback.print_exc()
            _respond({"id": request_id, "ok": False, "error": str(exc)})
        else:
            _respond(payload)


if __name__ == "__main__":
    main()
