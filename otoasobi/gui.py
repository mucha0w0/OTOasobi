from __future__ import annotations

import multiprocessing
import sys
from pathlib import Path

from PySide6.QtCore import QThread, QTimer, Qt, Signal
from PySide6.QtGui import QAction, QColor, QDragEnterEvent, QDropEvent, QIcon, QKeySequence, QPainter
from PySide6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QFileDialog,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QMainWindow,
    QMessageBox,
    QSplitter,
    QStatusBar,
    QTableWidget,
    QTableWidgetItem,
    QToolBar,
    QVBoxLayout,
    QWidget,
)

import numpy as np
import pyqtgraph as pg

from otoasobi.analyze import (
    Analysis,
    analyze_wav,
    default_log_path,
    default_txt_path,
    write_label_txt,
    write_log_txt,
)
from otoasobi.features import LOUDNESS_RANGE_DB, loudness_db, mel_spectrogram, spectrogram_tone
from otoasobi.parser import FilenameError
from otoasobi.phones import KIND_RGB, SILENCE_LABEL, phone_kind
from otoasobi.segment import Span

LEFT_AXIS_WIDTH = 52

ENGINE_LABEL = {
    "mms+praat": "高精度 (MMS+Praat)",
    "mms": "高精度 (MMS)",
    "praat": "Praat+音響",
    "acoustic": "音響",
}

LABEL_COLORS = KIND_RGB

STYLESHEET = """
QMainWindow, QWidget {
    background: #161616;
    color: #F3F4F6;
    font-family: "Yu Gothic UI", "Meiryo UI", sans-serif;
    font-size: 13px;
}
QToolBar {
    background: #202020;
    border: none;
    spacing: 8px;
    padding: 6px 10px;
}
QToolButton {
    background: #2A2A2A;
    color: #F3F4F6;
    border: 1px solid #3A3A3A;
    padding: 6px 14px;
    border-radius: 4px;
}
QToolButton:hover { background: #333333; }
QToolButton:pressed { background: #0F9D8E; color: #06110F; }
QTableWidget {
    background: #1A1A1A;
    alternate-background-color: #1F1F1F;
    gridline-color: #2E2E2E;
    border: none;
    selection-background-color: #0F9D8E;
    selection-color: #06110F;
}
QHeaderView::section {
    background: #202020;
    color: #D1D5DB;
    border: none;
    border-bottom: 1px solid #2E2E2E;
    padding: 6px 8px;
}
QStatusBar {
    background: #141414;
    color: #9CA3AF;
}
QMenuBar {
    background: #202020;
    color: #F3F4F6;
    padding: 4px 8px;
}
QMenuBar::item:selected { background: #2A2A2A; }
QMenu {
    background: #202020;
    color: #F3F4F6;
    border: 1px solid #3A3A3A;
}
QMenu::item:selected { background: #0F9D8E; color: #06110F; }
QSplitter::handle {
    background: #3A3A3A;
}
QSplitter::handle:hover {
    background: #0F9D8E;
}
QScrollBar:vertical, QScrollBar:horizontal {
    background: #161616;
    width: 10px;
    height: 10px;
}
QScrollBar::handle { background: #3F3F3F; border-radius: 4px; }
"""


def _icon_path() -> Path | None:
    candidates = [
        Path(sys.executable).parent / "app.ico",
        Path(__file__).resolve().parent.parent / "packaging" / "app.ico",
    ]
    for path in candidates:
        if path.exists():
            return path
    return None


class WaveformView(pg.PlotWidget):
    def __init__(self) -> None:
        super().__init__(background="#161616")
        self.setAcceptDrops(False)
        plot = self.getPlotItem()
        plot.showGrid(x=True, y=False, alpha=0.15)
        plot.setLabel("bottom", "時間", units="s")
        plot.setTitle("波形", color="#D1D5DB", size="10pt")
        _reserve_left_axis(plot, show_values=False)
        plot.getViewBox().setDefaultPadding(0.02)
        self._upper = pg.PlotCurveItem(pen=None)
        self._lower = pg.PlotCurveItem(pen=None)
        self._fill = pg.FillBetweenItem(self._lower, self._upper, brush=(230, 230, 230, 170))
        self.addItem(self._fill)
        self.addItem(self._upper)
        self.addItem(self._lower)
        self._regions: list[pg.LinearRegionItem] = []
        self._texts: list[pg.TextItem] = []
        self._playhead = pg.InfiniteLine(angle=90, movable=False, pen=pg.mkPen("#FBBF24", width=1.5))
        self.addItem(self._playhead)
        self._playhead.setVisible(False)

    def clear_labels(self) -> None:
        for item in self._regions + self._texts:
            self.removeItem(item)
        self._regions.clear()
        self._texts.clear()

    def set_empty(self) -> None:
        self.clear_labels()
        self._upper.setData([], [])
        self._lower.setData([], [])
        self._playhead.setVisible(False)
        self.setXRange(0, 1, padding=0)

    def set_analysis(self, analysis: Analysis) -> None:
        self.clear_labels()
        t, lo, hi = _envelope(analysis.y, analysis.sr)
        self._upper.setData(t, hi)
        self._lower.setData(t, lo)
        peak = max(0.2, float(np.max(np.abs(analysis.y))))
        for span in analysis.spans:
            if span.label == SILENCE_LABEL:
                continue
            rgb = LABEL_COLORS.get(phone_kind(span.label), (120, 120, 120))
            region = pg.LinearRegionItem(
                values=(span.start, span.end),
                brush=(*rgb, 55),
                pen=pg.mkPen((*rgb, 160), width=1),
                movable=False,
            )
            region.setZValue(-20)
            self.addItem(region)
            self._regions.append(region)
            if span.end - span.start >= 0.025:
                text = pg.TextItem(color="#F9FAFB", anchor=(0.5, 1))
                text.setHtml(f"<span style='font-family: Yu Gothic UI; font-size: 11px'>{span.label}</span>")
                text.setPos((span.start + span.end) / 2, peak * 0.92)
                self.addItem(text)
                self._texts.append(text)
        self.setYRange(-peak, peak, padding=0.04)
        self.setXRange(0, analysis.duration, padding=0.01)
        self._playhead.setVisible(False)

    def focus_span(self, span: Span) -> None:
        pad = max(0.05, (span.end - span.start) * 0.4)
        self.setXRange(max(0.0, span.start - pad), span.end + pad, padding=0)


def _reserve_left_axis(plot: pg.PlotItem, *, show_values: bool) -> None:
    axis = plot.getAxis("left")
    axis.setWidth(LEFT_AXIS_WIDTH)
    if show_values:
        return
    axis.setStyle(showValues=False, tickLength=0)
    axis.setPen(pg.mkPen("#161616"))


def _hz_ticks(centers_hz: np.ndarray, y0: float = 0.0) -> list[tuple[float, str]]:
    if len(centers_hz) == 0:
        return []
    ticks: list[tuple[float, str]] = []
    used: set[int] = set()
    lo = float(centers_hz[0])
    hi = float(centers_hz[-1])
    for hz in (125, 250, 500, 1000, 2000, 4000, 8000):
        if hz < lo * 0.7 or hz > hi * 1.15:
            continue
        idx = int(np.argmin(np.abs(centers_hz - hz)))
        if idx in used:
            continue
        used.add(idx)
        label = f"{hz / 1000:g}k" if hz >= 1000 else str(hz)
        ticks.append((idx + 0.5 + y0, label))
    return ticks


class MelSpectrogramView(pg.PlotWidget):
    def __init__(self) -> None:
        super().__init__(background="#161616")
        self.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform, False)
        self.setAcceptDrops(False)
        self.enableAutoRange(False)
        plot = self.getPlotItem()
        plot.showGrid(x=True, y=False, alpha=0.12)
        plot.setLabel("bottom", "時間", units="s")
        plot.setLabel("left", "Hz")
        plot.setTitle("メルスペクトログラム", color="#D1D5DB", size="10pt")
        _reserve_left_axis(plot, show_values=True)
        plot.getViewBox().setDefaultPadding(0.0)
        self._img = pg.ImageItem()
        self._img.setOpts(axisOrder="col-major")
        self._img.setLookupTable(pg.colormap.get("magma").getLookupTable(nPts=256))
        self._img.setZValue(-30)
        self.addItem(self._img)
        self._regions: list[pg.LinearRegionItem] = []
        self._texts: list[pg.TextItem] = []

    def clear_labels(self) -> None:
        for item in self._regions + self._texts:
            self.removeItem(item)
        self._regions.clear()
        self._texts.clear()

    def set_empty(self) -> None:
        self.clear_labels()
        self._img.clear()
        self.setXRange(0, 1, padding=0)
        self.setYRange(0, 1, padding=0)

    def set_analysis(self, analysis: Analysis) -> None:
        self.clear_labels()
        mel = mel_spectrogram(analysis.y, analysis.sr)
        db = mel.db
        n_mels = int(db.shape[0])
        tone = spectrogram_tone(db)
        self._img.setImage(np.ascontiguousarray(tone.T), autoLevels=False, levels=(0.0, 1.0))
        self._img.setRect(mel.t0, 0.0, max(mel.t1 - mel.t0, 1e-3), float(n_mels))
        for span in analysis.spans:
            if span.label == SILENCE_LABEL:
                continue
            rgb = LABEL_COLORS.get(phone_kind(span.label), (120, 120, 120))
            region = pg.LinearRegionItem(
                values=(span.start, span.end),
                brush=(*rgb, 42),
                pen=pg.mkPen((*rgb, 190), width=1),
                movable=False,
            )
            region.setZValue(5)
            self.addItem(region)
            self._regions.append(region)
            if span.end - span.start >= 0.025:
                text = pg.TextItem(anchor=(0.5, 1))
                text.setHtml(
                    "<span style='font-family: Yu Gothic UI; font-size: 11px; "
                    f"background-color: rgba(0,0,0,140); color: #F9FAFB'>{span.label}</span>"
                )
                text.setPos((span.start + span.end) / 2, n_mels * 0.96)
                text.setZValue(10)
                self.addItem(text)
                self._texts.append(text)
        self.getPlotItem().getAxis("left").setTicks([_hz_ticks(mel.centers_hz), []])
        self.setYRange(0, n_mels, padding=0.0)
        self.setXRange(0, max(analysis.duration, 1e-3), padding=0.01)


class LoudnessView(pg.PlotWidget):
    def __init__(self) -> None:
        super().__init__(background="#161616")
        self.setAcceptDrops(False)
        self.enableAutoRange(False)
        plot = self.getPlotItem()
        plot.showGrid(x=True, y=False, alpha=0.15)
        plot.setLabel("bottom", "時間", units="s")
        plot.setTitle("大きさ", color="#D1D5DB", size="10pt")
        _reserve_left_axis(plot, show_values=False)
        plot.getViewBox().setDefaultPadding(0.0)
        plot.getViewBox().setMouseEnabled(x=True, y=False)
        self._curve = pg.PlotCurveItem(
            pen=pg.mkPen("#99F6E4", width=1.6),
            fillLevel=0,
            brush=pg.mkBrush(153, 246, 228, 48),
        )
        self.addItem(self._curve)

    def set_empty(self) -> None:
        self._curve.setData([], [])
        self.setXRange(0, 1, padding=0)
        self.setYRange(0, 1, padding=0)

    def set_analysis(self, analysis: Analysis) -> None:
        loud = loudness_db(analysis.y, analysis.sr)
        loud_peak = float(np.max(loud)) if loud.size else 0.0
        norm = np.clip((loud - (loud_peak - LOUDNESS_RANGE_DB)) / LOUDNESS_RANGE_DB, 0.0, 1.0)
        times = (np.arange(len(loud)) + 0.5) / max(len(loud), 1) * analysis.duration
        self._curve.setData(times, norm)
        self.setYRange(0, 1, padding=0.06)
        self.setXRange(0, max(analysis.duration, 1e-3), padding=0.01)


class AnalyzeWorker(QThread):
    ok = Signal(object)
    err = Signal(str, str)

    def __init__(self, path: str) -> None:
        super().__init__()
        self.path = path

    def run(self) -> None:
        try:
            self.ok.emit(analyze_wav(self.path))
        except FilenameError as exc:
            self.err.emit("ファイル名エラー", str(exc))
        except Exception as exc:
            self.err.emit("解析エラー", f"{type(exc).__name__}: {exc}")


class WarmupWorker(QThread):
    done = Signal(bool)

    def run(self) -> None:
        try:
            from otoasobi.precision import warmup

            self.done.emit(bool(warmup()))
        except Exception:
            self.done.emit(False)


def _envelope(y: np.ndarray, sr: int, bins: int = 5000) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    y = np.asarray(y, dtype=np.float64)
    if len(y) == 0:
        return np.array([0.0]), np.array([0.0]), np.array([0.0])
    hop = max(1, len(y) // bins)
    n = len(y) // hop
    chunk = y[: n * hop].reshape(n, hop)
    t = (np.arange(n) + 0.5) * hop / sr
    return t, chunk.min(axis=1), chunk.max(axis=1)


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("音遊びラベル")
        self.resize(1180, 860)
        self.setMinimumSize(860, 640)
        self.setAcceptDrops(True)
        icon = _icon_path()
        if icon:
            self.setWindowIcon(QIcon(str(icon)))
        self.analysis: Analysis | None = None
        self._playing = False
        self._worker: AnalyzeWorker | None = None
        self._warmup: WarmupWorker | None = None
        self._precision_ready = False
        self._plot_heights_set = False
        self._build()
        self._start_warmup()

    def _build(self) -> None:
        toolbar = QToolBar()
        toolbar.setMovable(False)
        toolbar.setFloatable(False)
        self.addToolBar(toolbar)

        self.act_open = QAction("開く", self)
        self.act_open.setShortcut(QKeySequence.Open)
        self.act_open.triggered.connect(self.open_wav)
        self.act_play = QAction("再生", self)
        self.act_play.setShortcut(QKeySequence(Qt.Key.Key_Space))
        self.act_play.setShortcutContext(Qt.ShortcutContext.ApplicationShortcut)
        self.act_play.triggered.connect(self.toggle_play)
        self.act_save = QAction("保存", self)
        self.act_save.setShortcut(QKeySequence.Save)
        self.act_save.triggered.connect(self.save_txt)
        self.act_reanalyze = QAction("再解析", self)
        self.act_reanalyze.setShortcut(QKeySequence("F5"))
        self.act_reanalyze.triggered.connect(self.reanalyze)
        for action in (self.act_open, self.act_play, self.act_save, self.act_reanalyze):
            toolbar.addAction(action)

        self.file_label = QLabel("WAVを開くか、ここにドロップしてください")
        self.file_label.setStyleSheet("color: #9CA3AF; padding-left: 12px;")
        file_host = QWidget()
        file_layout = QHBoxLayout(file_host)
        file_layout.setContentsMargins(8, 0, 8, 0)
        file_layout.addWidget(self.file_label, 1)
        toolbar.addWidget(file_host)

        legend = QLabel("　".join(
            f"<span style='color: rgb{LABEL_COLORS[name]}'>■</span> {name}"
            for name in ("息", "子音", "母音")
        ))
        legend.setTextFormat(Qt.TextFormat.RichText)
        toolbar.addWidget(legend)

        splitter = QSplitter(Qt.Orientation.Vertical)
        splitter.setHandleWidth(8)
        splitter.setChildrenCollapsible(False)
        plots = QSplitter(Qt.Orientation.Vertical)
        plots.setHandleWidth(8)
        plots.setChildrenCollapsible(False)
        self.wave = WaveformView()
        self.mel = MelSpectrogramView()
        self.loud = LoudnessView()
        self.mel.setXLink(self.wave)
        self.loud.setXLink(self.wave)
        for view in (self.wave, self.mel, self.loud):
            view.setMinimumHeight(72)
        plots.addWidget(self.wave)
        plots.addWidget(self.mel)
        plots.addWidget(self.loud)
        plots.setStretchFactor(0, 2)
        plots.setStretchFactor(1, 3)
        plots.setStretchFactor(2, 1)
        self.plots = plots
        splitter.addWidget(plots)

        self.table = QTableWidget(0, 4)
        self.table.setHorizontalHeaderLabels(["開始 (秒)", "終了 (秒)", "音素", "かな"])
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.table.setAlternatingRowColors(True)
        self.table.verticalHeader().setVisible(False)
        header = self.table.horizontalHeader()
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.ResizeToContents)
        header.setSectionResizeMode(1, QHeaderView.ResizeMode.ResizeToContents)
        header.setSectionResizeMode(2, QHeaderView.ResizeMode.ResizeToContents)
        header.setSectionResizeMode(3, QHeaderView.ResizeMode.Stretch)
        self.table.itemSelectionChanged.connect(self._on_row_selected)
        splitter.addWidget(self.table)
        splitter.setStretchFactor(0, 5)
        splitter.setStretchFactor(1, 2)

        root = QWidget()
        layout = QVBoxLayout(root)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.addWidget(splitter)
        self.setCentralWidget(root)

        status = QStatusBar()
        self.setStatusBar(status)
        status.showMessage("高精度エンジンを準備しています…  ファイル名はひらがな・ヴなどのカタカナ・休符 _ のみです。")

        file_menu = self.menuBar().addMenu("ファイル")
        file_menu.addAction(self.act_open)
        file_menu.addAction(self.act_save)
        file_menu.addSeparator()
        quit_act = QAction("終了", self)
        quit_act.setShortcut(QKeySequence.Quit)
        quit_act.triggered.connect(self.close)
        file_menu.addAction(quit_act)
        view_menu = self.menuBar().addMenu("解析")
        view_menu.addAction(self.act_reanalyze)
        view_menu.addAction(self.act_play)

    def showEvent(self, event) -> None:
        super().showEvent(event)
        self._apply_plot_heights()

    def _apply_plot_heights(self) -> None:
        if self._plot_heights_set:
            return
        total = self.plots.height()
        if total <= 0:
            QTimer.singleShot(0, self._apply_plot_heights)
            return
        self._plot_heights_set = True
        wave = total * 2 // 6
        spec = total * 3 // 6
        loud = total - wave - spec
        self.plots.setSizes([wave, spec, loud])

    def _start_warmup(self) -> None:
        self._warmup = WarmupWorker()
        self._warmup.done.connect(self._on_warmup)
        self._warmup.start()

    def _on_warmup(self, ok: bool) -> None:
        self._precision_ready = bool(ok)
        if self.analysis is not None:
            return
        if ok:
            self.statusBar().showMessage("高精度エンジンの準備ができました。WAVを開くか、ドロップしてください。")
        else:
            self.statusBar().showMessage("音響エンジンで解析します。ファイル名はひらがな・ヴなどのカタカナ・休符 _ のみです。")

    def _set_busy(self, busy: bool) -> None:
        for action in (self.act_open, self.act_save, self.act_reanalyze):
            action.setEnabled(not busy)
        if busy:
            QApplication.setOverrideCursor(Qt.CursorShape.WaitCursor)
        else:
            QApplication.restoreOverrideCursor()

    def dragEnterEvent(self, event: QDragEnterEvent) -> None:
        if event.mimeData().hasUrls():
            event.acceptProposedAction()

    def dropEvent(self, event: QDropEvent) -> None:
        for url in event.mimeData().urls():
            path = url.toLocalFile()
            if path.lower().endswith(".wav"):
                self.load_path(path)
                return

    def open_wav(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "WAVを開く", "", "WAV (*.wav)")
        if path:
            self.load_path(path)

    def load_path(self, path: str) -> None:
        if self._worker is not None and self._worker.isRunning():
            return
        self._stop_play()
        self._set_busy(True)
        engine_hint = "高精度" if self._precision_ready else "音響"
        self.statusBar().showMessage(f"解析中（{engine_hint}）…")
        self._worker = AnalyzeWorker(path)
        self._worker.ok.connect(self._on_analyzed)
        self._worker.err.connect(self._on_analyze_failed)
        self._worker.finished.connect(lambda: self._set_busy(False))
        self._worker.start()

    def _on_analyzed(self, analysis: Analysis) -> None:
        self.analysis = analysis
        self.file_label.setText(analysis.path.name)
        self.wave.set_analysis(analysis)
        self.mel.set_analysis(analysis)
        self.loud.set_analysis(analysis)
        self._fill_table(analysis.spans)
        txt = default_txt_path(analysis.path)
        log = default_log_path(analysis.path)
        write_label_txt(txt, analysis.spans)
        write_log_txt(log, analysis.spans)
        self.statusBar().showMessage(self._summary(analysis) + f"  →  {txt.name} に保存しました")

    def _on_analyze_failed(self, title: str, message: str) -> None:
        if title == "ファイル名エラー":
            QMessageBox.warning(self, title, message)
        else:
            QMessageBox.critical(self, title, message)

    def reanalyze(self) -> None:
        if self.analysis is None:
            self.open_wav()
            return
        self.load_path(str(self.analysis.path))

    def save_txt(self) -> None:
        if self.analysis is None:
            QMessageBox.information(self, "保存", "先にWAVを開いてください。")
            return
        suggested = str(default_txt_path(self.analysis.path))
        path, _ = QFileDialog.getSaveFileName(self, "ラベルを保存", suggested, "テキスト (*.txt)")
        if not path:
            return
        write_label_txt(path, self.analysis.spans)
        log = Path(path).with_suffix(".log.txt")
        write_log_txt(log, self.analysis.spans)
        self.statusBar().showMessage(f"保存しました: {path}")

    def toggle_play(self) -> None:
        if self.analysis is None:
            return
        if self._playing:
            self._stop_play()
            return
        try:
            import winsound

            winsound.PlaySound(str(self.analysis.path), winsound.SND_FILENAME | winsound.SND_ASYNC)
            self._playing = True
            self.act_play.setText("停止")
        except Exception as exc:
            QMessageBox.critical(self, "再生エラー", str(exc))

    def _stop_play(self) -> None:
        if not self._playing:
            return
        try:
            import winsound

            winsound.PlaySound(None, winsound.SND_PURGE)
        except Exception:
            pass
        self._playing = False
        self.act_play.setText("再生")

    def closeEvent(self, event) -> None:
        self._stop_play()
        super().closeEvent(event)

    def _fill_table(self, spans: list[Span]) -> None:
        self.table.setRowCount(len(spans))
        for row, span in enumerate(spans):
            values = (f"{span.start:.3f}", f"{span.end:.3f}", span.label, span.token)
            for col, value in enumerate(values):
                item = QTableWidgetItem(value)
                item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                if col == 2:
                    rgb = LABEL_COLORS.get(phone_kind(span.label), (180, 180, 180))
                    item.setForeground(QColor(*rgb))
                self.table.setItem(row, col, item)

    def _on_row_selected(self) -> None:
        if self.analysis is None:
            return
        rows = self.table.selectionModel().selectedRows()
        if not rows:
            return
        index = rows[0].row()
        if 0 <= index < len(self.analysis.spans):
            self.wave.focus_span(self.analysis.spans[index])

    def _summary(self, analysis: Analysis) -> str:
        counts = {"無音": 0, "息": 0, "子音": 0, "母音": 0}
        for span in analysis.spans:
            kind = phone_kind(span.label)
            counts[kind] = counts.get(kind, 0) + 1
        mora_n = sum(1 for token in analysis.tokens if not token.is_rest)
        rest_n = sum(1 for token in analysis.tokens if token.is_rest)
        engine = ENGINE_LABEL.get(analysis.engine, analysis.engine)
        return (
            f"{engine} / {analysis.duration:.2f}秒 / モーラ{mora_n} / 休符{rest_n} / "
            f"無音{counts['無音']} 息{counts['息']} 子音{counts['子音']} 母音{counts['母音']}"
        )


def main(argv: list[str] | None = None) -> None:
    multiprocessing.freeze_support()
    argv = sys.argv if argv is None else [sys.argv[0], *argv]
    app = QApplication(argv)
    app.setApplicationName("音遊びラベル")
    app.setStyleSheet(STYLESHEET)
    pg.setConfigOptions(antialias=True, foreground="#D1D5DB")
    icon = _icon_path()
    if icon:
        app.setWindowIcon(QIcon(str(icon)))
    window = MainWindow()
    window.show()
    paths = [a for a in argv[1:] if not a.startswith("-")]
    if paths:
        window.load_path(paths[0])
    raise SystemExit(app.exec())


if __name__ == "__main__":
    main()
