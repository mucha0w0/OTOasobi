import sys
from pathlib import Path

from otoasobi.analyze import (
    analyze_wav,
    default_log_path,
    default_txt_path,
    write_label_txt,
    write_log_txt,
)
from otoasobi.oto import mono_oto_lines


def main() -> None:
    if len(sys.argv) < 2:
        print("usage: python -m otoasobi.cli <file.wav>")
        sys.exit(1)
    path = Path(sys.argv[1])
    analysis = analyze_wav(path)
    out = default_txt_path(path)
    log = default_log_path(path)
    write_label_txt(out, analysis.spans)
    write_log_txt(log, analysis.spans)
    sys.stdout.reconfigure(encoding="utf-8")
    print("\n".join(mono_oto_lines(path.name, analysis.spans, analysis.duration)), end="\n")
    print(f"engine {analysis.engine}", file=sys.stderr)
    print(f"wrote {out}", file=sys.stderr)
    print(f"wrote {log}", file=sys.stderr)


if __name__ == "__main__":
    main()
