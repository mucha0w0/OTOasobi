from __future__ import annotations

import sys
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg, NavigationToolbar2Tk
from matplotlib.figure import Figure

from otoasobi.analyze import Analysis, analyze_wav, default_txt_path, write_label_txt
from otoasobi.parser import FilenameError
from otoasobi.plotting import apply_jp_font, draw_analysis


class LabelApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("音遊び ラベル付け")
        self.geometry("1100x680")
        self.minsize(820, 520)
        self.analysis: Analysis | None = None
        self._build()

    def _build(self) -> None:
        bar = ttk.Frame(self, padding=(10, 8))
        bar.pack(fill=tk.X)
        ttk.Button(bar, text="WAVを開く", command=self.open_wav).pack(side=tk.LEFT)
        ttk.Button(bar, text="再解析", command=self.reanalyze).pack(side=tk.LEFT, padx=(8, 0))
        ttk.Button(bar, text="再生", command=self.play).pack(side=tk.LEFT, padx=(8, 0))
        ttk.Button(bar, text="ラベルを保存", command=self.save_txt).pack(side=tk.LEFT, padx=(8, 0))
        self.path_var = tk.StringVar(value="WAVファイルを開いてください")
        ttk.Label(bar, textvariable=self.path_var).pack(side=tk.LEFT, padx=16)

        self.fig = Figure(figsize=(11, 5), dpi=100)
        self.canvas = FigureCanvasTkAgg(self.fig, master=self)
        toolbar = NavigationToolbar2Tk(self.canvas, self, pack_toolbar=False)
        toolbar.update()
        toolbar.pack(fill=tk.X)
        self.canvas.get_tk_widget().pack(fill=tk.BOTH, expand=True)

        self.status = tk.StringVar(value="ファイル名はひらがなと休符 _ と、ヴなどのカタカナのみです。")
        ttk.Label(self, textvariable=self.status, padding=(10, 6)).pack(fill=tk.X)
        self._draw_empty()

    def _draw_empty(self) -> None:
        apply_jp_font()
        self.fig.clear()
        ax = self.fig.add_subplot(111)
        ax.set_title("波形とラベル")
        ax.set_xlabel("時間 (秒)")
        ax.set_ylabel("振幅")
        self.fig.tight_layout()
        self.canvas.draw_idle()

    def open_wav(self) -> None:
        path = filedialog.askopenfilename(
            title="WAVを開く",
            filetypes=[("WAV", "*.wav"), ("すべて", "*.*")],
        )
        if not path:
            return
        self.load_path(path)

    def load_path(self, path: str) -> None:
        try:
            analysis = analyze_wav(path)
        except FilenameError as exc:
            messagebox.showerror("ファイル名エラー", str(exc))
            return
        except Exception as exc:
            messagebox.showerror("解析エラー", f"{type(exc).__name__}: {exc}")
            return
        self.analysis = analysis
        self.path_var.set(analysis.path.name)
        self._show(analysis)
        txt = default_txt_path(analysis.path)
        write_label_txt(txt, analysis.spans)
        self.status.set(self._summary(analysis) + f"  → {txt.name} に保存済み")

    def reanalyze(self) -> None:
        if self.analysis is None:
            self.open_wav()
            return
        self.load_path(str(self.analysis.path))

    def save_txt(self) -> None:
        if self.analysis is None:
            messagebox.showinfo("保存", "先にWAVを開いてください。")
            return
        path = filedialog.asksaveasfilename(
            title="ラベルを保存",
            defaultextension=".txt",
            initialfile=default_txt_path(self.analysis.path).name,
            filetypes=[("テキスト", "*.txt")],
        )
        if not path:
            return
        write_label_txt(path, self.analysis.spans)
        self.status.set(f"保存しました: {path}")

    def play(self) -> None:
        if self.analysis is None:
            return
        try:
            import winsound

            winsound.PlaySound(str(self.analysis.path), winsound.SND_FILENAME | winsound.SND_ASYNC)
        except Exception as exc:
            messagebox.showerror("再生エラー", str(exc))

    def _show(self, analysis: Analysis) -> None:
        draw_analysis(self.fig, analysis.y, analysis.sr, analysis.spans, analysis.path.name)
        self.canvas.draw_idle()

    def _summary(self, analysis: Analysis) -> str:
        counts = {"無音": 0, "子音": 0, "母音": 0}
        for span in analysis.spans:
            counts[span.label] = counts.get(span.label, 0) + 1
        mora_n = sum(1 for t in analysis.tokens if not t.is_rest)
        rest_n = sum(1 for t in analysis.tokens if t.is_rest)
        return (
            f"{analysis.duration:.2f}秒 / モーラ{mora_n} / 休符{rest_n} / "
            f"無音{counts['無音']} 子音{counts['子音']} 母音{counts['母音']}"
        )


def main(argv: list[str] | None = None) -> None:
    argv = sys.argv[1:] if argv is None else argv
    app = LabelApp()
    if argv:
        app.after(80, lambda: app.load_path(argv[0]))
    app.mainloop()


if __name__ == "__main__":
    main()
