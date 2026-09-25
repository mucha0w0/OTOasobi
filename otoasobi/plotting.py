from __future__ import annotations

from matplotlib import font_manager
import matplotlib.pyplot as plt
from matplotlib.figure import Figure
import numpy as np

from otoasobi.phones import phone_kind
from otoasobi.segment import Span

LABEL_COLORS = {
    "無音": "#9AA0A6",
    "息": "#60A5FA",
    "子音": "#D97706",
    "母音": "#0F9D8E",
}


def pick_jp_font() -> str | None:
    wanted = {"Yu Gothic", "Yu Gothic UI", "Meiryo", "MS Gothic", "Noto Sans CJK JP"}
    available = {f.name for f in font_manager.fontManager.ttflist}
    for name in wanted:
        if name in available:
            return name
    return None


def apply_jp_font() -> None:
    name = pick_jp_font()
    if name:
        plt.rcParams["font.family"] = name
    plt.rcParams["axes.unicode_minus"] = False


def draw_analysis(fig: Figure, y, sr: int, spans: list[Span], title: str) -> None:
    apply_jp_font()
    fig.clear()
    ax = fig.add_subplot(111)
    y = np.asarray(y)
    duration = len(y) / sr if sr else 0.0
    times = np.arange(len(y)) / sr if sr else np.array([])
    ax.plot(times, y, color="#1F2937", linewidth=0.6, alpha=0.9)
    ymax = max(0.2, float(np.max(np.abs(y))) if len(y) else 0.2)
    for span in spans:
        ax.axvspan(
            span.start,
            span.end,
            color=LABEL_COLORS.get(phone_kind(span.label), "#CCCCCC"),
            alpha=0.28,
            linewidth=0,
        )
        mid = (span.start + span.end) / 2
        if span.end - span.start >= 0.025:
            ax.text(
                mid,
                ymax * 0.86,
                span.label,
                ha="center",
                va="top",
                fontsize=8,
                color="#111827",
            )
    ax.set_xlim(0, duration)
    ax.set_ylim(-ymax * 1.05, ymax * 1.05)
    ax.set_xlabel("時間 (秒)")
    ax.set_ylabel("振幅")
    ax.set_title(title)
    ax.axhline(0, color="#D1D5DB", linewidth=0.6)
    handles = [
        plt.Rectangle((0, 0), 1, 1, color=LABEL_COLORS[name], alpha=0.45, label=name)
        for name in ("無音", "息", "子音", "母音")
    ]
    ax.legend(handles=handles, loc="lower right", framealpha=0.92)
    fig.tight_layout()
