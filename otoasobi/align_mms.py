from __future__ import annotations

import threading
from dataclasses import dataclass

import numpy as np

from otoasobi.phones import MmsUnit

_LOCK = threading.Lock()
_BUNDLE = None
_MODEL = None
_ALIGNER = None
_TOKENIZER = None


def available() -> bool:
    try:
        import torch  # noqa: F401
        import torchaudio  # noqa: F401
        from torchaudio.pipelines import MMS_FA  # noqa: F401
    except Exception:
        return False
    return True


def load_model() -> None:
    global _BUNDLE, _MODEL, _ALIGNER, _TOKENIZER
    with _LOCK:
        if _MODEL is not None:
            return
        from torchaudio.pipelines import MMS_FA as bundle

        _BUNDLE = bundle
        _MODEL = bundle.get_model()
        _MODEL.eval()
        _ALIGNER = bundle.get_aligner()
        _TOKENIZER = bundle.get_tokenizer()


@dataclass(frozen=True)
class AlignedChar:
    char: str
    time: float
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
        span = group[0]
        out.append(
            AlignedChar(
                char=unit.char,
                time=float(span.start) * spf,
                score=float(span.score),
                token_index=unit.token_index,
                role=unit.role,
            )
        )
    return out
