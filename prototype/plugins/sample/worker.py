import json
import sys
from pathlib import Path


def main() -> None:
    for line in sys.stdin:
        text = line.strip()
        if not text:
            continue
        message = json.loads(text)
        project = message.get("project") or ""
        root = Path(project)
        wavs = len(list(root.glob("*.wav"))) if root.is_dir() else 0
        sys.stdout.write(json.dumps({"wavs": wavs}, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
