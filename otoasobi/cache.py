"""フォルダ内の区間を OTOasibi_cache.json に残す。

次回同じフォルダを開いたときは、このファイルを読み、WAV は再解析しない。
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from otoasobi.wavs import AnalyzedFile, list_wavs
from otoasobi.segment import Span

CACHE_NAME = "OTOasibi_cache.json"
CACHE_VERSION = 1


@dataclass
class CacheLoad:
    files: list[AnalyzedFile]
    notice: str | None = None


def file_to_mapping(item: AnalyzedFile) -> dict:
    return {
        "name": item.name,
        "duration": item.duration,
        "engine": item.engine,
        "spans": [
            {"start": span.start, "end": span.end, "label": span.label, "token": span.token}
            for span in item.spans
        ],
    }


def file_from_mapping(item: object) -> AnalyzedFile:
    if not isinstance(item, dict):
        raise RuntimeError("修正データの形式が違います。")
    name = item.get("name")
    if not isinstance(name, str) or not name or "/" in name or "\\" in name or name.startswith("."):
        raise RuntimeError("ファイル名が不正です。")
    try:
        duration = float(item.get("duration", 0))
    except (TypeError, ValueError) as exc:
        raise RuntimeError(f"{name} の長さが不正です。") from exc
    raw = item.get("spans")
    if not isinstance(raw, list) or not raw:
        raise RuntimeError(f"{name} の区間がありません。")
    if duration < 0:
        duration = 0.0
    spans = [_span_from_mapping(row, duration) for row in raw]
    if duration <= 0:
        duration = max(span.end for span in spans)
    engine = item.get("engine", "acoustic")
    if not isinstance(engine, str) or not engine.strip():
        engine = "acoustic"
    return AnalyzedFile(name=name, duration=duration, spans=spans, engine=engine.strip())


def _cache_files_on_disk(folder: Path) -> list[AnalyzedFile]:
    path = folder / CACHE_NAME
    if not path.is_file():
        return []
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        return []
    if not isinstance(data, dict) or data.get("version") != CACHE_VERSION:
        return []
    raw = data.get("files")
    if not isinstance(raw, list) or not raw:
        return []
    files: list[AnalyzedFile] = []
    for item in raw:
        try:
            files.append(file_from_mapping(item))
        except RuntimeError:
            continue
    return files


def merge_cache(folder: str | Path, updates: list[AnalyzedFile]) -> Path:
    """updates で上書きし、既存の OTOasibi_cache.json は他のファイル分を残す。"""
    folder = Path(folder)
    existing = _cache_files_on_disk(folder)
    by_name = {item.name.casefold(): item for item in existing}
    for item in updates:
        by_name[item.name.casefold()] = item
    ordered: list[AnalyzedFile] = []
    seen: set[str] = set()
    for item in existing:
        key = item.name.casefold()
        if key not in by_name:
            continue
        ordered.append(by_name[key])
        seen.add(key)
    for item in updates:
        key = item.name.casefold()
        if key in seen:
            continue
        ordered.append(item)
        seen.add(key)
    return save_cache(folder, ordered)


def save_cache(folder: str | Path, files: list[AnalyzedFile]) -> Path:
    folder = Path(folder)
    if not folder.is_dir():
        raise RuntimeError("フォルダが見つかりません。")
    if not files:
        raise RuntimeError("保存する区間がありません。")
    path = folder / CACHE_NAME
    payload = {
        "version": CACHE_VERSION,
        "files": [file_to_mapping(item) for item in files],
    }
    text = json.dumps(payload, ensure_ascii=False, indent=2) + "\n"
    tmp = path.with_name(path.name + ".tmp")
    try:
        tmp.write_text(text, encoding="utf-8", newline="\n")
        tmp.replace(path)
    except Exception:
        tmp.unlink(missing_ok=True)
        raise
    return path


def read_cache(folder: str | Path) -> CacheLoad | None:
    """ファイルが無ければ None。あるが使えないときは RuntimeError。"""
    folder = Path(folder)
    path = folder / CACHE_NAME
    if not path.is_file():
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(
            "保存データ（OTOasibi_cache.json）を読めませんでした。削除してから、もう一度開いてください。"
        ) from exc
    if not isinstance(data, dict) or data.get("version") != CACHE_VERSION:
        raise RuntimeError(
            "保存データの形式が対応していません。OTOasibi_cache.json を削除してから、もう一度開いてください。"
        )
    raw = data.get("files")
    if not isinstance(raw, list) or not raw:
        raise RuntimeError(
            "保存データに区間がありません。OTOasibi_cache.json を削除してから、もう一度開いてください。"
        )
    try:
        files = [file_from_mapping(item) for item in raw]
    except RuntimeError as exc:
        raise RuntimeError(
            f"{exc} OTOasibi_cache.json を削除してから、もう一度開いてください。"
        ) from exc
    return _match_wavs(folder, files)


def _match_wavs(folder: Path, files: list[AnalyzedFile]) -> CacheLoad:
    on_disk = {path.name.casefold(): path.name for path in list_wavs(folder)}
    loaded: list[AnalyzedFile] = []
    seen: set[str] = set()
    missing: list[str] = []
    for item in files:
        key = item.name.casefold()
        actual = on_disk.get(key)
        if actual is None:
            missing.append(item.name)
            continue
        if key in seen:
            continue
        seen.add(key)
        item.name = actual
        loaded.append(item)
    if not loaded:
        raise RuntimeError(
            "保存データにある WAV が、このフォルダにありません。OTOasibi_cache.json を削除してから、もう一度開いてください。"
        )
    extra = [name for key, name in on_disk.items() if key not in seen]
    return CacheLoad(files=loaded, notice=_mismatch_notice(missing, extra))


def _mismatch_notice(missing: list[str], extra: list[str]) -> str | None:
    parts: list[str] = []
    if missing:
        parts.append(f"保存データのうち {len(missing)} 件はフォルダに無いため、読み飛ばしました。")
    if extra:
        parts.append(f"フォルダ内の {len(extra)} 件は保存データに無いため、含まれていません。")
    if not parts:
        return None
    return "\n".join(parts)


def _span_from_mapping(row: object, duration: float) -> Span:
    if not isinstance(row, dict):
        raise RuntimeError("区間の形式が違います。")
    try:
        start = float(row["start"])
        end = float(row["end"])
    except (KeyError, TypeError, ValueError) as exc:
        raise RuntimeError("区間の時刻が不正です。") from exc
    label = row.get("label")
    token = row.get("token", "")
    if not isinstance(label, str) or not isinstance(token, str):
        raise RuntimeError("区間のラベルが不正です。")
    if duration > 0:
        start = min(max(0.0, start), duration)
        end = min(max(0.0, end), duration)
    if end < start:
        start, end = end, start
    if end <= start:
        end = min(duration, start + 0.001) if duration > 0 else start + 0.001
        if end <= start:
            start = max(0.0, end - 0.001)
    return Span(start=start, end=end, label=label, token=token)
