from __future__ import annotations

import os
import shutil
import threading
import zipfile
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from otoasobi.phones import MmsUnit

_LOCK = threading.Lock()
_BUNDLE = None
_MODEL = None
_ALIGNER = None
_TOKENIZER = None
# torch.hub が落とす model.pt。途中までの .partial はこれより小さい。
_MIN_CHECKPOINT_BYTES = 1_000_000_000
_CHECKPOINT_NAME = "model.pt"


def available() -> bool:
    try:
        import torch  # noqa: F401
        import torchaudio  # noqa: F401
        from torchaudio.pipelines import MMS_FA  # noqa: F401
    except Exception:
        return False
    return True


def model_is_loaded() -> bool:
    return _MODEL is not None


def _checkpoint_ready(path: Path, min_bytes: int) -> bool:
    try:
        if not path.is_file() or path.stat().st_size < min_bytes:
            return False
    except OSError:
        return False
    return zipfile.is_zipfile(path)


def _discard_partials(directory: Path) -> None:
    try:
        partials = list(directory.glob("*.partial"))
    except OSError:
        return
    for path in partials:
        try:
            path.unlink()
        except OSError:
            pass


def place_checkpoint(dest: Path, sources: list[Path], min_bytes: int = _MIN_CHECKPOINT_BYTES) -> bool:
    """Put a complete model.pt at dest, using a local copy when one exists."""
    if _checkpoint_ready(dest, min_bytes):
        _discard_partials(dest.parent)
        return True
    if dest.is_file():
        try:
            dest.unlink()
        except OSError:
            return False
    try:
        dest_key = dest.resolve()
    except OSError:
        dest_key = dest
    for src in sources:
        try:
            if src.resolve() == dest_key:
                continue
        except OSError:
            continue
        if not _checkpoint_ready(src, min_bytes):
            continue
        dest.parent.mkdir(parents=True, exist_ok=True)
        try:
            os.link(src, dest)
        except OSError:
            try:
                shutil.copyfile(src, dest)
            except OSError:
                continue
        if _checkpoint_ready(dest, min_bytes):
            _discard_partials(dest.parent)
            return True
    return _checkpoint_ready(dest, min_bytes)


def _torch_checkpoint() -> Path:
    home = os.environ.get("TORCH_HOME")
    if home:
        root = Path(home)
    else:
        xdg = os.environ.get("XDG_CACHE_HOME")
        root = Path(xdg) / "torch" if xdg else Path.home() / ".cache" / "torch"
    return root / "hub" / "checkpoints" / _CHECKPOINT_NAME


def _checkpoint_sources() -> list[Path]:
    sources = [Path.home() / ".cache" / "torch" / "hub" / "checkpoints" / _CHECKPOINT_NAME]
    xdg = os.environ.get("XDG_CACHE_HOME")
    if xdg:
        sources.append(Path(xdg) / "torch" / "hub" / "checkpoints" / _CHECKPOINT_NAME)
    local = os.environ.get("LOCALAPPDATA")
    if local:
        sources.append(Path(local) / "Otoasobi" / "models" / "hub" / "checkpoints" / _CHECKPOINT_NAME)
    return sources


def ensure_mms_checkpoint() -> bool:
    """TORCH_HOME に完成した model.pt がなければ、既にあるコピーを置く。

    WinUI は TORCH_HOME をアプリのデータフォルダにする。そこからは
    ~/.cache/torch が見えないので、get_model が 1.2GB を取り直す。
    転送が止まると、音素対応の表示のまま戻ってこない。
    """
    dest = _torch_checkpoint()
    ready = place_checkpoint(dest, _checkpoint_sources())
    if not ready:
        return False
    _discard_partials(dest.parent)
    local = os.environ.get("LOCALAPPDATA")
    if local:
        _discard_partials(Path(local) / "Otoasobi" / "models" / "hub" / "checkpoints")
    return True


def load_model() -> None:
    global _BUNDLE, _MODEL, _ALIGNER, _TOKENIZER
    with _LOCK:
        if _MODEL is not None:
            return
        ready = ensure_mms_checkpoint()
        from torchaudio.pipelines import MMS_FA as bundle

        previous_timeout = None
        if not ready:
            import socket

            previous_timeout = socket.getdefaulttimeout()
            # データを受け取らない接続で、音素対応の表示のまま終わらなくする。
            socket.setdefaulttimeout(60)
        try:
            _BUNDLE = bundle
            _MODEL = bundle.get_model()
            _MODEL.eval()
            _ALIGNER = bundle.get_aligner()
            _TOKENIZER = bundle.get_tokenizer()
        finally:
            if not ready:
                import socket

                socket.setdefaulttimeout(previous_timeout)


@dataclass(frozen=True)
class AlignedChar:
    char: str
    time: float
    end: float
    score: float
    token_index: int
    role: str


def align_chars(y: np.ndarray, sr: int, units: list[MmsUnit]) -> list[AlignedChar]:
    if not units:
        return []
    import torch
    import torchaudio

    load_model()
    assert _BUNDLE is not None and _MODEL is not None
    assert _ALIGNER is not None and _TOKENIZER is not None

    wav = torch.from_numpy(np.asarray(y, dtype=np.float32)).unsqueeze(0)
    if wav.ndim > 2:
        wav = wav.mean(dim=1, keepdim=True)
    if sr != _BUNDLE.sample_rate:
        wav = torchaudio.functional.resample(wav, sr, _BUNDLE.sample_rate)

    letters = [u.char for u in units]
    token_ids = _TOKENIZER(letters)
    with torch.inference_mode():
        emission, _ = _MODEL(wav)
        raw_spans = _ALIGNER(emission[0], token_ids)

    duration = float(len(y) / sr)
    n_frames = int(emission.shape[1])
    if n_frames <= 0:
        raise RuntimeError("MMS emission is empty")
    spf = duration / n_frames

    if len(raw_spans) != len(units):
        raise RuntimeError(f"MMS span count {len(raw_spans)} != {len(units)}")

    out: list[AlignedChar] = []
    for unit, group in zip(units, raw_spans, strict=True):
        if not group:
            raise RuntimeError(f"MMS returned no span for {unit.char!r}")
        start_t = float(group[0].start) * spf
        end_t = float(group[-1].end) * spf
        if end_t <= start_t:
            end_t = start_t + spf
        out.append(
            AlignedChar(
                char=unit.char,
                time=start_t,
                end=end_t,
                score=float(group[0].score),
                token_index=unit.token_index,
                role=unit.role,
            )
        )
    return out
