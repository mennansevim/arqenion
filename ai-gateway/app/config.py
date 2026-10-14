"""Ayarlar: ortam değişkenleri ve isteğe bağlı .env dosyası."""
import os
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent          # ai-gateway/
REPO = ROOT.parent                                      # arqenion/


def _load_dotenv(path: Path) -> None:
    """Basit .env okuyucu (ek paket gerektirmez). Ortamda zaten olan değeri ezmez."""
    if not path.exists():
        return
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


_load_dotenv(ROOT / ".env")


@dataclass(frozen=True)
class Settings:
    model_dir: Path = Path(os.getenv("PROCEDURE_MODEL_DIR", REPO / "procedure-model" / "dist"))
    model_version: str = os.getenv("PROCEDURE_MODEL_VERSION", "2.4")
    engine_dir: Path = Path(os.getenv("PROCEDURE_ENGINE_DIR", REPO / "procedure-model" / "tools"))
    openai_api_key: str = os.getenv("OPENAI_API_KEY", "")
    openai_model: str = os.getenv("OPENAI_MODEL", "gpt-4o-mini")
    openai_base_url: str = os.getenv("OPENAI_BASE_URL", "")   # boş = OpenAI; Azure/vekil sunucu için doldurulur
    ai_timeout_sec: float = float(os.getenv("AI_TIMEOUT_SEC", "6"))
    log_dir: Path = Path(os.getenv("AI_LOG_DIR", ROOT / "logs"))
    # Alet adında geçmeyen niteleyiciler (ör. Ochsner-Kocher → "dişli"); backend ile aynı dosya.
    descriptors_path: Path = Path(os.getenv("DESCRIPTORS_PATH", REPO / "backend" / "Procedures" / "instrument-descriptors.json"))


settings = Settings()
