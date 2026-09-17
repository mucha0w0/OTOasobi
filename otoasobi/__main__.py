from pathlib import Path
import sys

from otoasobi.analyze import analyze_wav, default_txt_path, spans_to_txt, write_label_txt


def main() -> None:
    if len(sys.argv) < 2:
        print("usage: python -m otoasobi <file.wav>")
        sys.exit(1)
    path = Path(sys.argv[1])
    analysis = analyze_wav(path)
    print("stem", analysis.stem)
    print("tokens", [(t.kind, t.text) for t in analysis.tokens])
    txt = spans_to_txt(analysis.spans)
    print(txt)
    out = default_txt_path(path)
    write_label_txt(out, analysis.spans)
    print("wrote", out)


if __name__ == "__main__":
    main()
