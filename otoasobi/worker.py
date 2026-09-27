"""常駐の解析プロセス。WinUI アプリが標準入出力の JSON で呼び出す。

1行に1オブジェクト。
要求: {"id": 1, "cmd": "analyze", "path": "C:/音.wav"}
応答: {"id": 1, "ok": true, "txt": "C:/音.txt"}
      {"id": 1, "ok": false, "error": "..."}
"""

from __future__ import annotations

import json
import sys
import traceback

from otoasobi.analyze import analyze_wav, default_txt_path, write_label_txt
from otoasobi.parser import FilenameError


def _respond(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def _analyze(path: str) -> str:
    analysis = analyze_wav(path)
    out = default_txt_path(path)
    write_label_txt(out, analysis.spans)
    return str(out)


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
            txt = _analyze(path)
        except FilenameError as exc:
            _respond({"id": request_id, "ok": False, "error": str(exc)})
        except Exception as exc:
            traceback.print_exc()
            _respond({"id": request_id, "ok": False, "error": str(exc)})
        else:
            _respond({"id": request_id, "ok": True, "txt": txt})


if __name__ == "__main__":
    main()
