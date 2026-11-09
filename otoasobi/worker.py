"""常駐の解析プロセス。ホストが標準入出力の JSON で呼び出す。

1行に1オブジェクト。
要求: {"id": 1, "cmd": "analyze", "path": "C:/音声フォルダ"}
途中: {"id": 1, "event": "progress", "done": 1, "total": 4}
      {"id": 1, "event": "file", "name": "あ.wav", "done": 0.4, "label": "特徴を計算しています"}
file の done は、いま解析しているファイルの 0 から 1。
応答: {"id": 1, "ok": true, "files": [...], "notice": "..."}
      {"id": 1, "ok": false, "error": "..."}

analyze は oto.ini を書かない。区間は OTOasibi_cache.json に残す。
同じフォルダに保存データがあるときは再解析せず、そのファイルを返す。
"force": true のときだけ解析し直し、保存データを上書きする。
"only" がファイル名、またはファイル名の配列のときは、そのファイルだけ解析し直す。応答の files はその件数で、保存データは書かない。
要求: {"id": 2, "cmd": "preview", "path": "C:/音声フォルダ/あ.wav", "t0": 0, "t1": 1.2, "columns": 1600, "rows": 400}
preview の t0/t1 は表示中の秒。columns/rows は画面のピクセル数。
波形はその幅の最小最大で、サンプルの方が少なければ 1 点ずつ返す。
spec が無いスペクトラムは、同じ範囲をそのピクセル数へ最近傍で描いた PNG です。
spec が "index" のときは、ファイルごとのメルを 1 回計算して範囲を切り出し、色番号を返す。
その計算がまだ無いときは、先に {"event": "wave"} で波形だけ返す。横は 1024 列まで。
wave が false のときは波形を返さず、色番号だけ返す。
同じファイルの preview が続けて溜まっているときは、古い要求へ skipped を返してから最新だけ計算する。
計算の途中で次の preview が来たときも、そのメルは残さず skipped を返す。
読み込んだ波形と、計算したメルと音量は、直近 12 ファイルを残す。時刻か大きさが変わったら読み直す。
要求: {"id": 3, "cmd": "save", "folder": "C:/音声フォルダ", "files": [...]}
要求: {"id": 4, "cmd": "write", "folder": "C:/音声フォルダ", "files": [...], "config": {...}}
config は offset_ms（ミリ秒。正は手前、負は後で、±100ms）、consonant_ms（ミリ秒）、blank（秒、-0.1 から 0.1）、overlap、mode。
mode が renzoku のときは連続音、cvvc のときは CVVC。それ以外は単独音。
単独音の offset_ms の 0 は子音開始。overlap は子音ラベルから、0 が子音の開始、1 が母音の開始。負の数は子音の開始より前の秒で、オフセットより前にも置く。1 個の数値なら全子音がその割合で、無い子音は 0.5。
連続音の offset_ms の 0 は、その音素の子音と直前の母音の境界から 150ms 手前。子音が無いときは母音と母音の境界から 150ms 手前。overlap は 1 個の数値で、0 がオフセットの位置、1 がその音素の開始。
連続音の rest は単独音と同じ形の config。アンダーバーだけの無音の直後の音素だけ、この値で単独音の計算をする。エイリアスは連続音のまま。
CVVC の lead は単独音と同じ形の config。発音の先頭の音素だけ、この値で単独音の計算をする。エイリアスは「- さ」の形。
あいうえおんは、アンダーバーの直後を除き、連続音と同じ計算です。エイリアスは CVVC のままです。
そのオフセットとオーバーラップは vc_offset_ms と vc_overlap、子音部とブランクは consonant_ms と blank です。
CVVC の consonant_ms と blank は通常音素。overlap は子音ラベルから 0〜1 で、0 が子音の開始、1 が先行発声。負の秒は使わない。
CVVC の vc_offset_ms と vc_overlap は子音音素で、連続音のオフセットとオーバーラップと同じ。母音音素もこの 2 つを使う。vc_consonant と vc_blank は子音ラベルから 0〜1。
blank の 0 は音量の上辺の右端。正は減衰側、負は手前へ、その秒数だけずらす。子音部とブランクは単独音と連続音と CVVC の通常音素で同じ。
応答の txt は oto.ini。parts があるときは、その順のエイリアスを 1 つのファイルに足す。全区間は OTOasibi_log.txt、母音始まりは OTOasibi_out.txt。
画面で区間を直したあとは save が保存データを更新し、write が oto.ini を計算して書く。
write の adjustments は、ドラッグで直した位置。file、index、alias と、直した点だけの秒。無い点は自動推定のまま。name は手で直したエイリアス。編集印の * は oto.ini に書かない。
要求: {"id": 5, "cmd": "frq", "folder": "C:/音声フォルダ", "mode": "all", "only": null, "clear": null}
frq はフォルダ直下の WAV に、同じ名前の .frq（FREQ0003）を書く。original は見ない。区間や oto.ini は変えない。
mode は all（上書き）、missing（.frq が無いものだけ）、selected（only のファイルを上書き）。only はファイル名か、その配列。
clear は、ファイル名と [開始秒, 終了秒] の組。推定のあと、その範囲の周波数を 0 にしてから書く。null のときは触らない。
応答: {"id": 5, "ok": true, "written": 12, "notice": "..."}
書く対象が無く、失敗もないときは ok が true で written は 0。失敗だけで 1 件も書けないときは ok が false。
"""

from __future__ import annotations

import json
import os
import sys
import threading
import traceback
from collections import deque
from pathlib import Path

# torch.hub の進捗表示は改行しない。標準エラーのパイプが埋まると、音素対応のまま止まる。
os.environ.setdefault("TQDM_DISABLE", "1")

from otoasobi.analyze import (
    AnalysisCancelled,
    analyze_folder_files,
    analyze_wav,
    export_folder,
    skipped_notice,
)
from otoasobi.wavs import AnalyzedFile
from otoasobi.cache import file_from_mapping, file_to_mapping, read_cache, save_cache
from otoasobi.frq import write_folder_frq
from otoasobi.oto import adjustments_from, oto_settings_from
from otoasobi.parser import FilenameError
from otoasobi.preview_worker import preview_payload


def _respond(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def _wav_in_folder(folder: str, name: object) -> Path:
    if not isinstance(name, str) or not name or "/" in name or "\\" in name or name.startswith("."):
        raise RuntimeError("ファイル名が不正です。")
    if Path(name).name != name or Path(name).suffix.lower() != ".wav":
        raise RuntimeError("WAV ファイルを選んでください。")
    root = Path(folder)
    if not root.is_dir():
        raise RuntimeError("フォルダが見つかりません。")
    wav = (root / name).resolve()
    if wav.parent != root.resolve():
        raise RuntimeError("ファイル名が不正です。")
    if not wav.is_file():
        raise RuntimeError("選んだ音声が見つかりません。")
    return wav


def _note_file(request_id: object, shown: dict, name: str, fraction: float, label: str) -> None:
    """同じファイルと同じ段階で、進み 0.02 未満なら進捗を出さない。"""
    done = round(max(0.0, min(1.0, float(fraction))), 3)
    if (
        shown.get("name", "") == name
        and shown["label"] == label
        and done < 1.0
        and done - float(shown["done"]) < 0.02
    ):
        return
    shown["name"] = name
    shown["done"] = done
    shown["label"] = label
    _respond(
        {
            "id": request_id,
            "event": "file",
            "name": name,
            "done": done,
            "label": label,
        }
    )


def _only_names(only: object) -> list[str]:
    if isinstance(only, str):
        values: list[object] = [only]
    elif isinstance(only, list):
        values = list(only)
    else:
        raise RuntimeError("ファイル名が不正です。")
    names: list[str] = []
    seen: set[str] = set()
    for item in values:
        if not isinstance(item, str) or not item:
            raise RuntimeError("ファイル名が不正です。")
        key = item.casefold()
        if key in seen:
            continue
        seen.add(key)
        names.append(item)
    if not names:
        raise RuntimeError("ファイルを選んでください。")
    return names


def _analyze_only(request_id: object, folder: str, only: object) -> dict:
    """選んだファイルだけ解析する。保存データは画面側がまとめて書く。"""
    wavs: list[Path] = []
    seen: set[str] = set()
    for name in _only_names(only):
        wav = _wav_in_folder(folder, name)
        key = wav.name.casefold()
        if key in seen:
            continue
        seen.add(key)
        wavs.append(wav)

    def on_progress(done: int, total: int) -> None:
        _respond({"id": request_id, "event": "progress", "done": done, "total": total})

    shown = {"name": "", "done": -1.0, "label": ""}
    files: list[AnalyzedFile] = []
    failed: list[str] = []
    total = len(wavs)
    for index, wav in enumerate(wavs):
        if _pull_cancels():
            return {"id": request_id, "ok": True, "cancelled": True}
        on_progress(index, total)

        def on_step(fraction: float, label: str, name: str = wav.name) -> None:
            _note_file(request_id, shown, name, fraction, label)

        try:
            analysis = analyze_wav(wav, on_step)
        except Exception as exc:
            failed.append(f"{wav.name}（{exc}）")
            continue
        files.append(
            AnalyzedFile(
                name=wav.name,
                duration=analysis.duration,
                spans=analysis.spans,
                engine=analysis.engine,
            )
        )
    on_progress(total, total)
    if not files:
        return {
            "id": request_id,
            "ok": False,
            "error": "\n".join(failed) or "解析できませんでした。",
        }
    payload: dict = {
        "id": request_id,
        "ok": True,
        "files": [file_to_mapping(item) for item in files],
    }
    if failed:
        payload["notice"] = f"{len(failed)}個のファイルをスキップしました。\n" + "\n".join(failed)
    return payload


def _analyze(request_id: object, path: str, force: bool) -> dict:
    if not force:
        loaded = read_cache(path)
        if loaded is not None:
            payload: dict = {
                "id": request_id,
                "ok": True,
                "cached": True,
                "files": [file_to_mapping(item) for item in loaded.files],
            }
            if loaded.notice:
                payload["notice"] = loaded.notice
            return payload

    def on_progress(done: int, total: int) -> None:
        _respond({"id": request_id, "event": "progress", "done": done, "total": total})

    shown = {"name": "", "done": -1.0, "label": ""}

    def on_file(name: str, fraction: float, label: str) -> None:
        _note_file(request_id, shown, name, fraction, label)

    try:
        files, skipped = analyze_folder_files(path, on_progress=on_progress, on_file=on_file, stop=_pull_cancels)
    except AnalysisCancelled:
        return {"id": request_id, "ok": True, "cancelled": True}
    if not files:
        return {"id": request_id, "ok": False, "error": skipped_notice(skipped)}
    notes: list[str] = []
    if skipped:
        notes.append(skipped_notice(skipped))
    try:
        save_cache(path, files)
    except Exception as exc:
        notes.append(f"保存データを書けませんでした。{exc}")
    payload = {
        "id": request_id,
        "ok": True,
        "files": [file_to_mapping(item) for item in files],
    }
    if notes:
        payload["notice"] = "\n".join(notes)
    return payload


def _preview_waiting() -> bool:
    with _lines_cv:
        for line in _lines:
            try:
                request = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(request, dict) and request.get("cmd") == "preview":
                return True
    return False


def _preview(path: str, request: dict, request_id: object) -> dict:
    return preview_payload(path, request, request_id, _respond, _preview_waiting)


def _peek_command(line: str) -> tuple[str | None, dict | None]:
    try:
        request = json.loads(line)
    except json.JSONDecodeError:
        return None, None
    if not isinstance(request, dict):
        return None, None
    command = request.get("cmd")
    return (command if isinstance(command, str) else None), request


def _pull_cancels() -> bool:
    """キューに入った cancel を取り出して応答し、ほかの行は順に戻す。"""
    found = False
    with _lines_cv:
        kept: deque[str] = deque()
        while _lines:
            line = _lines.popleft()
            command, request = _peek_command(line)
            if command == "cancel" and request is not None:
                found = True
                _respond({"id": request.get("id"), "ok": True, "cancelled": True})
            else:
                kept.append(line)
        _lines.extend(kept)
    return found


def _parsed_files(files: object) -> list[AnalyzedFile]:
    if not isinstance(files, list) or not files:
        raise RuntimeError("修正データがありません。")
    return [file_from_mapping(item) for item in files]


def _save(folder: str, files: object) -> dict:
    parsed = _parsed_files(files)
    save_cache(folder, parsed)
    return {"ok": True}


def _clear_ranges(value: object) -> dict[str, list[tuple[float, float]]] | None:
    if value is None:
        return None
    if not isinstance(value, list):
        raise RuntimeError("先行発声の範囲が不正です。")
    found: dict[str, list[tuple[float, float]]] = {}
    for item in value:
        if not isinstance(item, dict):
            continue
        name = item.get("name")
        if not isinstance(name, str) or not name or Path(name).name != name:
            continue
        ranges: list[tuple[float, float]] = []
        raw = item.get("ranges")
        if isinstance(raw, list):
            for pair in raw:
                if not isinstance(pair, list) or len(pair) != 2:
                    continue
                start, end = pair
                if isinstance(start, bool) or isinstance(end, bool):
                    continue
                if isinstance(start, (int, float)) and isinstance(end, (int, float)):
                    ranges.append((float(start), float(end)))
        found[name.casefold()] = ranges
    return found


def _frq(request_id: object, folder: str, request: dict) -> dict:
    def on_progress(done: int, total: int) -> None:
        _respond({"id": request_id, "event": "progress", "done": done, "total": total})

    shown = {"name": "", "done": -1.0, "label": ""}

    def on_file(name: str, fraction: float, label: str) -> None:
        _note_file(request_id, shown, name, fraction, label)

    mode = request.get("mode")
    if not isinstance(mode, str) or not mode:
        mode = "all"
    only = request.get("only")
    if only is not None and not isinstance(only, (str, list)):
        raise RuntimeError("ファイル名が不正です。")
    batch = write_folder_frq(
        folder,
        on_progress=on_progress,
        on_file=on_file,
        mode=mode,
        only=only,
        clear=_clear_ranges(request.get("clear")),
    )
    notes = list(batch.notes)
    if batch.failed:
        names = "\n".join(batch.failed)
        notes.append(f"{len(batch.failed)}個のファイルをスキップしました。スキップしたファイル↓\n{names}")
    notice = "\n".join(notes) if notes else None
    if batch.written == 0 and batch.failed:
        return {
            "id": request_id,
            "ok": False,
            "written": 0,
            "error": notice or "frqを書けませんでした。",
        }
    payload: dict = {"id": request_id, "ok": True, "written": batch.written}
    if notice:
        payload["notice"] = notice
    return payload


def _write_parts(raw: object) -> list[tuple] | None:
    """選んだエイリアス。各要素は config と、そのエイリアスの adjustments。"""
    if not isinstance(raw, list) or not raw:
        return None
    parts: list[tuple] = []
    for item in raw:
        if not isinstance(item, dict):
            continue
        parts.append((oto_settings_from(item.get("config")), adjustments_from(item.get("adjustments"))))
    return parts or None


def _scope_name(raw: object) -> str:
    if raw in ("missing", "overlap", "all"):
        return str(raw)
    return "all"


def _sync(folder: str, files: object, parts: object) -> dict:
    parsed = _parsed_files(files)
    built = _write_parts(parts)
    if not built:
        raise RuntimeError("書き出すエイリアスがありません。")
    out = export_folder(folder, parsed, parts=built, scope="all", logs=False)
    return {"ok": True, "txt": str(out)}


def _write(
    folder: str,
    files: object,
    config: object,
    adjustments: object = None,
    parts: object = None,
    scope: object = None,
) -> dict:
    parsed = _parsed_files(files)
    chosen = _scope_name(scope)
    built = _write_parts(parts)
    if built:
        out = export_folder(folder, parsed, parts=built, scope=chosen)
    else:
        out = export_folder(
            folder,
            parsed,
            oto_settings_from(config),
            adjustments_from(adjustments),
            scope=chosen,
        )
    try:
        save_cache(folder, parsed)
    except Exception as exc:
        return {
            "ok": True,
            "txt": str(out),
            "notice": f"oto.ini は生成しましたが、保存データを更新できませんでした。{exc}",
        }
    return {"ok": True, "txt": str(out)}


_lines: deque[str] = deque()
_lines_cv = threading.Condition()
_stdin_closed = False


def _read_stdin() -> None:
    global _stdin_closed
    for line in sys.stdin:
        with _lines_cv:
            _lines.append(line)
            _lines_cv.notify()
    with _lines_cv:
        _stdin_closed = True
        _lines_cv.notify()


def _pull_line(block: bool) -> str | None:
    with _lines_cv:
        while not _lines and not _stdin_closed:
            if not block:
                return None
            _lines_cv.wait()
        if not _lines:
            return None
        return _lines.popleft()


def _parse_request(line: str) -> dict | None:
    line = line.strip()
    if not line:
        return None
    try:
        request = json.loads(line)
    except json.JSONDecodeError as exc:
        _respond({"id": None, "ok": False, "error": f"不正な要求です: {exc}"})
        return None
    if not isinstance(request, dict):
        _respond({"id": None, "ok": False, "error": "不正な要求です"})
        return None
    return request


def _coalesce_preview(request: dict) -> dict:
    """溜まった preview は最新だけ残し、古い id には skipped を返す。"""
    if request.get("cmd") != "preview":
        return request
    while True:
        line = _pull_line(False)
        if line is None:
            return request
        nxt = _parse_request(line)
        if nxt is None:
            continue
        if nxt.get("cmd") != "preview":
            with _lines_cv:
                _lines.appendleft(line)
            return request
        _respond({"id": request.get("id"), "ok": True, "skipped": True})
        request = nxt


def _dispatch(request: dict) -> None:
    request_id = request.get("id")
    try:
        command = request.get("cmd")
        if command == "analyze":
            path = request.get("path")
            if not isinstance(path, str) or not path:
                raise RuntimeError("path がありません")
            only = request.get("only")
            if only is not None:
                payload = _analyze_only(request_id, path, only)
            else:
                payload = _analyze(request_id, path, request.get("force") is True)
        elif command == "cancel":
            payload = {"ok": True, "cancelled": True}
        elif command == "preview":
            path = request.get("path")
            if not isinstance(path, str) or not path:
                raise RuntimeError("path がありません")
            payload = _preview(path, request, request_id)
        elif command == "save":
            folder = request.get("folder")
            if not isinstance(folder, str) or not folder:
                raise RuntimeError("folder がありません")
            payload = _save(folder, request.get("files"))
        elif command == "frq":
            folder = request.get("folder")
            if not isinstance(folder, str) or not folder:
                raise RuntimeError("folder がありません")
            payload = _frq(request_id, folder, request)
        elif command == "sync":
            folder = request.get("folder")
            if not isinstance(folder, str) or not folder:
                raise RuntimeError("folder がありません")
            payload = _sync(folder, request.get("files"), request.get("parts"))
        elif command == "write":
            folder = request.get("folder")
            if not isinstance(folder, str) or not folder:
                raise RuntimeError("folder がありません")
            payload = _write(
                folder,
                request.get("files"),
                request.get("config"),
                request.get("adjustments"),
                request.get("parts"),
                request.get("scope"),
            )
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


def main() -> None:
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    threading.Thread(target=_read_stdin, name="otoasobi-stdin", daemon=True).start()
    while True:
        line = _pull_line(True)
        if line is None:
            break
        request = _parse_request(line)
        if request is None:
            continue
        _dispatch(_coalesce_preview(request))


if __name__ == "__main__":
    main()
