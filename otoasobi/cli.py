from pathlib import Path
import sys

from otoasobi.analyze import analyze_wav, default_txt_path, spans_to_txt, write_label_txt


def main() -> None:
    if len(sys.argv) < 2:
        print("usage: python -m otoasobi.cli <file.wav>")
        sys.exit(1)
    path = Path(sys.argv[1])
    analysis = analyze_wav(path)
    out = default_txt_path(path)
    write_label_txt(out, analysis.spans)
    sys.stdout.reconfigure(encoding="utf-8")
    print(spans_to_txt(analysis.spans), end="")
    print(f"wrote {out}", file=sys.stderr)


if __name__ == "__main__":
    main()
