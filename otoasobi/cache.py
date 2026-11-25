"""フォルダの作業ファイル OTOasobi.otoasobi に区間を残す。

作業中の原音設定と録音の選択も同じファイル。中身は JSON。
次回同じフォルダを開いたときは、この区間を読み、WAV は再解析しない。
"""

from __future__ import annotations

import json
import time
from dataclasses import dataclass
from pathlib import Path

from otoasobi.wavs import AnalyzedFile, list_wavs
from otoasobi.segment import Span

PROJECT_NAME = "OTOasobi.otoasobi"
PROJECT_VERSION = 1
CACHE_VERSION = 1
LEGACY_CACHE_NAMES = ("OTOasobi_cache.json", "OTOasibi_cache.json")
_FILE_LABEL = "OTOasobi.otoasobi"


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


def _project_path(folder: Path, project: str | None = None) -> Path:
    if project:
        return Path(project)
    return folder / PROJECT_NAME


def _legacy_cache_path(folder: Path) -> Path | None:
    for name in LEGACY_CACHE_NAMES:
        path = folder / name
        if path.is_file():
            return path
    return None


def _cache_payload(data: object) -> dict | None:
    if not isinstance(data, dict):
        return None
    if "files" in data and "session" not in data and "recording" not in data and data.get("version") == CACHE_VERSION:
        return data
    cache = data.get("cache")
    if isinstance(cache, dict) and cache.get("version") == CACHE_VERSION:
        return cache
    return None


def _cache_files_on_disk(folder: Path, project: str | None = None) -> list[AnalyzedFile]:
    path = _project_path(folder, project)
    data: object | None = None
    if path.is_file():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError):
            data = None
    payload = _cache_payload(data) if data is not None else None
    if payload is None:
        legacy = _legacy_cache_path(folder)
        if legacy is None:
            return []
        try:
            data = json.loads(legacy.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError):
            return []
        payload = data if isinstance(data, dict) else None
    if not isinstance(payload, dict) or payload.get("version") != CACHE_VERSION:
        return []
    raw = payload.get("files")
    if not isinstance(raw, list) or not raw:
        return []
    files: list[AnalyzedFile] = []
    for item in raw:
        try:
            files.append(file_from_mapping(item))
        except RuntimeError:
            continue
    return files


def merge_cache(folder: str | Path, updates: list[AnalyzedFile], project: str | None = None) -> Path:
    """updates で上書きし、既存の区間は他のファイル分を残す。"""
    folder = Path(folder)
    existing = _cache_files_on_disk(folder, project)
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
    return save_cache(folder, ordered, project)


def save_cache(folder: str | Path, files: list[AnalyzedFile], project: str | None = None) -> Path:
    folder = Path(folder)
    if not folder.is_dir():
        raise RuntimeError("フォルダが見つかりません。")
    if not files:
        raise RuntimeError("保存する区間がありません。")
    cache = {
        "version": CACHE_VERSION,
        "files": [file_to_mapping(item) for item in files],
    }

    def change(document: dict) -> None:
        document["cache"] = cache

    _update_document(folder, change, project)
    for name in LEGACY_CACHE_NAMES:
        (folder / name).unlink(missing_ok=True)
    return _project_path(folder, project)


def read_cache(folder: str | Path, project: str | None = None) -> CacheLoad | None:
    """区間が無ければ None。あるが使えないときは RuntimeError。"""
    folder = Path(folder)
    path = _project_path(folder, project)
    legacy = _legacy_cache_path(folder)
    if path.is_file():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise RuntimeError(
                f"保存データ（{_FILE_LABEL}）を読めませんでした。削除してから、もう一度開いてください。"
            ) from exc
        if isinstance(data, dict) and "cache" not in data:
            if legacy is not None:
                return _read_cache_body(folder, json.loads(legacy.read_text(encoding="utf-8")))
            return None
        payload = data.get("cache") if isinstance(data, dict) else None
    elif legacy is not None:
        try:
            payload = json.loads(legacy.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise RuntimeError(
                f"保存データ（{_FILE_LABEL}）を読めませんでした。削除してから、もう一度開いてください。"
            ) from exc
    else:
        return None
    return _read_cache_body(folder, payload)


def _read_cache_body(folder: Path, payload: object) -> CacheLoad | None:
    if not isinstance(payload, dict) or payload.get("version") != CACHE_VERSION:
        raise RuntimeError(
            f"保存データの形式が対応していません。{_FILE_LABEL} を削除してから、もう一度開いてください。"
        )
    raw = payload.get("files")
    if not isinstance(raw, list) or not raw:
        raise RuntimeError(
            f"保存データに区間がありません。{_FILE_LABEL} を削除してから、もう一度開いてください。"
        )
    try:
        files = [file_from_mapping(item) for item in raw]
    except RuntimeError as exc:
        raise RuntimeError(
            f"{exc} {_FILE_LABEL} を削除してから、もう一度開いてください。"
        ) from exc
    return _match_wavs(folder, files)


def _update_document(folder: Path, change, project: str | None = None) -> None:
    path = _project_path(folder, project)
    last: Exception | None = None
    for _ in range(50):
        try:
            _update_locked(path, change)
            return
        except PermissionError as exc:
            last = exc
            time.sleep(0.02)
    raise RuntimeError("作業ファイルを開けませんでした。") from last


def _update_locked(path: Path, change) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a+b") as handle:
        _lock(handle)
        try:
            handle.seek(0)
            raw = handle.read()
            document: dict = {"version": PROJECT_VERSION}
            if raw.strip():
                loaded = json.loads(raw.decode("utf-8"))
                if not isinstance(loaded, dict):
                    raise RuntimeError("作業ファイルの形式が違います。")
                document = loaded
            if not isinstance(document.get("version"), int) or document["version"] > PROJECT_VERSION:
                raise RuntimeError("この保存データは、いまの OTOasobi では読めません。")
            document["version"] = PROJECT_VERSION
            change(document)
            text = json.dumps(document, ensure_ascii=False, indent=2) + "\n"
            handle.seek(0)
            handle.truncate()
            handle.write(text.encode("utf-8"))
            handle.flush()
        finally:
            _unlock(handle)


def _lock(handle) -> None:
    import msvcrt

    handle.seek(0, 2)
    if handle.tell() == 0:
        handle.write(b" ")
        handle.flush()
    handle.seek(0)
    msvcrt.locking(handle.fileno(), msvcrt.LK_LOCK, 1)


def _unlock(handle) -> None:
    import msvcrt

    handle.seek(0)
    try:
        msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
    except OSError:
        pass


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
            f"保存データにある WAV が、このフォルダにありません。{_FILE_LABEL} を削除してから、もう一度開いてください。"
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
