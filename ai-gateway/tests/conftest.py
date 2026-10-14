import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
os.environ.setdefault("AI_LOG_DIR", tempfile.mkdtemp(prefix="ai-gateway-test-logs-"))  # testler gerçek kayda yazmasın
os.environ["OPENAI_API_KEY"] = ""  # testler hiçbir zaman gerçek yapay zekâyı çağırmaz
