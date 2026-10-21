"""常駐の解析プロセス。WinUI アプリが標準入出力の JSON で呼び出す。

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
"only": "あ.wav" のときは、その 1 ファイルだけ解析し直す。応答の files はその 1 件で、保存データは書かない。
要求: {"id": 2, "cmd": "preview", "path": "C:/音声フォルダ/あ.wav", "t0": 0, "t1": 1.2, "columns": 1600, "rows": 400}
preview の t0/t1 は表示中の秒。columns/rows は画面のピクセル数。
波形はその幅の最小最大で、サンプルの方が少なければ 1 点ずつ返す。
スペクトラムは同じ範囲をそのピクセル数へ最近傍で描き、拡大しても引き伸ばさない。
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
応答の txt はそのモードの oto.ini。全区間は OTOasibi_log.txt、母音始まりは OTOasibi_out.txt。
画面で区間を直したあとは save が保存データを更新し、write が oto.ini を計算して書く。
write の adjustments は、ドラッグで直した位置。file、index、alias と、直した点だけの秒。無い点は自動推定のまま。name は手で直したエイリアス。編集印の * は oto.ini に書かない。
要求: {"id": 5, "cmd": "frq", "folder": "C:/音声フォルダ"}
frq はフォルダ直下の WAV ごとに、同じ名前の .frq（FREQ0003）を書く。original は見ない。区間や oto.ini は変えない。
応答: {"id": 5, "ok": true, "written": 12, "notice": "..."}
1 件も書けないときは ok が false。一部だけの失敗は ok が true で、notice に名前が入る。
"""

from __future__ import annotations

import base64
import json
import os
import sys
import traceback
from pathlib import Path

from otoasobi.analyze import (
    AnalyzedFile,
    analyze_folder_files,
    analyze_wav,
    export_folder,
    load_wav,
    skipped_notice,
)
from otoasobi.cache import file_from_mapping, file_to_mapping, read_cache, save_cache
from otoasobi.frq import write_folder_frq
from otoasobi.oto import adjustments_from, oto_settings_from
from otoasobi.parser import FilenameError
from otoasobi.preview import render_preview


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


def _analyze_one(request_id: object, folder: str, name: object) -> dict:
    """表示中の 1 ファイルだけ解析する。保存データは画面側がまとめて書く。"""
    wav = _wav_in_folder(folder, name)

    def on_progress(done: int, total: int) -> None:
        _respond({"id": request_id, "event": "progress", "done": done, "total": total})

    shown = {"name": "", "done": -1.0, "label": ""}

    def on_step(fraction: float, label: str) -> None:
        _note_file(request_id, shown, wav.name, fraction, label)

    on_progress(0, 1)
    analysis = analyze_wav(wav, on_step)
    on_progress(1, 1)
    item = AnalyzedFile(
        name=wav.name,
        duration=analysis.duration,
        spans=analysis.spans,
        engine=analysis.engine,
    )
    return {
        "id": request_id,
        "ok": True,
        "files": [file_to_mapping(item)],
    }


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

    files, skipped = analyze_folder_files(path, on_progress=on_progress, on_file=on_file)
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


_wav_cache: tuple[str, int, int, object, int] | None = None


def _load_cached(path: str) -> tuple[np.ndarray, int]:
    global _wav_cache
    st = os.stat(path)
    key = (path, st.st_mtime_ns, st.st_size)
    if _wav_cache is not None and _wav_cache[0] == key[0] and _wav_cache[1] == key[1] and _wav_cache[2] == key[2]:
        return _wav_cache[3], _wav_cache[4]
    y, sr = load_wav(path)
    _wav_cache = (key[0], key[1], key[2], y, sr)
    return y, sr


def _optional_float(value: object) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return float(value)


def _optional_int(value: object, default: int, lo: int, hi: int) -> int:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return default
    return max(lo, min(hi, int(value)))


def _preview(path: str, request: dict) -> dict:
    y, sr = _load_cached(path)
    duration = len(y) / float(sr) if sr else 0.0
    t0 = _optional_float(request.get("t0"))
    t1 = _optional_float(request.get("t1"))
    if t0 is None:
        t0 = 0.0
    if t1 is None or t1 <= t0:
        t1 = duration
    columns = _optional_int(request.get("columns"), 1600, 32, 4096)
    rows = _optional_int(request.get("rows"), 320, 32, 1200)
    preview = render_preview(y, sr, t0, t1, columns, rows)
    payload = {
        "ok": True,
        "duration": duration,
        "wave_lo": preview.wave_lo,
        "wave_hi": preview.wave_hi,
        "wave_t0": preview.wave_t0,
        "wave_t1": preview.wave_t1,
        "wave_samples": preview.samples,
        "sr": preview.sr,
        "spec_png": base64.b64encode(preview.png).decode("ascii"),
        "spec_t0": preview.spec_t0,
        "spec_t1": preview.spec_t1,
    }
    if preview.loudness:
        payload["loudness"] = preview.loudness
    return payload


def _parsed_files(files: object) -> list[AnalyzedFile]:
    if not isinstance(files, list) or not files:
        raise RuntimeError("修正データがありません。")
    return [file_from_mapping(item) for item in files]


def _save(folder: str, files: object) -> dict:
    parsed = _parsed_files(files)
    save_cache(folder, parsed)
    return {"ok": True}


def _frq(request_id: object, folder: str) -> dict:
    def on_progress(done: int, total: int) -> None:
        _respond({"id": request_id, "event": "progress", "done": done, "total": total})

    shown = {"name": "", "done": -1.0, "label": ""}

    def on_file(name: str, fraction: float, label: str) -> None:
        _note_file(request_id, shown, name, fraction, label)

    written, failed = write_folder_frq(folder, on_progress=on_progress, on_file=on_file)
    notice = None
    if failed:
        names = "\n".join(failed)
        notice = f"{len(failed)}個のファイルをスキップしました。スキップしたファイル↓\n{names}"
    if written == 0:
        return {
            "id": request_id,
            "ok": False,
            "written": 0,
            "error": notice or "frqを書けませんでした。",
        }
    payload: dict = {"id": request_id, "ok": True, "written": written}
    if notice:
        payload["notice"] = notice
    return payload


def _write(folder: str, files: object, config: object, adjustments: object = None) -> dict:
    parsed = _parsed_files(files)
    out = export_folder(folder, parsed, oto_settings_from(config), adjustments_from(adjustments))
    try:
        save_cache(folder, parsed)
    except Exception as exc:
        return {
            "ok": True,
            "txt": str(out),
            "notice": f"oto.ini は作成しましたが、保存データを更新できませんでした。{exc}",
        }
    return {"ok": True, "txt": str(out)}


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
            command = request.get("cmd")
            if command == "analyze":
                path = request.get("path")
                if not isinstance(path, str) or not path:
                    raise RuntimeError("path がありません")
                only = request.get("only")
                if only is not None:
                    payload = _analyze_one(request_id, path, only)
                else:
                    payload = _analyze(request_id, path, request.get("force") is True)
            elif command == "preview":
                path = request.get("path")
                if not isinstance(path, str) or not path:
                    raise RuntimeError("path がありません")
                payload = _preview(path, request)
            elif command == "save":
                folder = request.get("folder")
                if not isinstance(folder, str) or not folder:
                    raise RuntimeError("folder がありません")
                payload = _save(folder, request.get("files"))
            elif command == "frq":
                folder = request.get("folder")
                if not isinstance(folder, str) or not folder:
                    raise RuntimeError("folder がありません")
                payload = _frq(request_id, folder)
            elif command == "write":
                folder = request.get("folder")
                if not isinstance(folder, str) or not folder:
                    raise RuntimeError("folder がありません")
                payload = _write(
                    folder,
                    request.get("files"),
                    request.get("config"),
                    request.get("adjustments"),
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


if __name__ == "__main__":
    main()
