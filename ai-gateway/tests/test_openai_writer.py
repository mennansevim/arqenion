"""OpenAI yazıcısını gerçek anahtar olmadan dener: yerel sahte bir sunucu OpenAI gibi cevap verir."""
import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

import pytest

from app.service import FeedbackService
from app.writer import OpenAIWriter

DRAFT = {
    "possibleRisk": "Emilmeyen iplik fasyada yabancı cisim reaksiyonu yapabilir.",
    "explanation": "Fasya için 0 Vicryl istenmişti; Prolen emilmeyen bir ipliktir.",
    "recommendedAction": "Prolen'i geri alıp 0 numara iğneli Vicryl hazırla.",
    "instruments": ["SUTURE_POLYGLACTIN_0"],
    "sources": ["open-appendectomy@2.4.0:error:S52-E6"],
}


class FakeOpenAI(BaseHTTPRequestHandler):
    seen = []

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        FakeOpenAI.seen.append(body)
        reply = {"id": "x", "object": "chat.completion", "created": 0, "model": body["model"],
                 "choices": [{"index": 0, "finish_reason": "stop",
                              "message": {"role": "assistant", "content": json.dumps(DRAFT, ensure_ascii=False)}}],
                 "usage": {"prompt_tokens": 900, "completion_tokens": 80, "total_tokens": 980}}
        data = json.dumps(reply).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *args):
        pass


@pytest.fixture(scope="module")
def fake_server():
    server = HTTPServer(("127.0.0.1", 0), FakeOpenAI)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    yield f"http://127.0.0.1:{server.server_port}/v1"
    server.shutdown()


def test_openai_writer_end_to_end(fake_server):
    writer = OpenAIWriter("sk-test", "gpt-4o-mini", timeout=3, base_url=fake_server)
    ans = FeedbackService(writer=writer).explain({"stepId": 52, "deviationType": "WRONG_INSTRUMENT",
                                                  "expected": "SUTURE_POLYGLACTIN_0", "actual": "SUTURE_POLYPROPYLENE_0"})
    assert ans["source"] == "llm", ans["checks"]
    assert ans["explanation"] == DRAFT["explanation"]
    assert ans["promptTokens"] == 900 and ans["modelName"] == "gpt-4o-mini"
    prompt = FakeOpenAI.seen[-1]["messages"][1]["content"]
    assert "[open-appendectomy@2.4.0:error:S52-E6]" in prompt      # kaynak kimlikleriyle gönderildi
    fmt = FakeOpenAI.seen[-1]["response_format"]
    assert fmt["type"] == "json_schema" and fmt["json_schema"]["strict"] is True
    schema = fmt["json_schema"]["schema"]
    allowed = schema["properties"]["instruments"]["items"]["enum"]   # yalnızca bu adımda beklenen aletler
    assert "SUTURE_POLYGLACTIN_0" in allowed and "SUTURE_POLYPROPYLENE_0" not in allowed
    assert "open-appendectomy@2.4.0:error:S52-E6" in schema["properties"]["sources"]["items"]["enum"]


def test_schema_without_expected_instruments_has_no_instruments_field():
    from app.writer import build_schema, build_user_prompt
    schema = build_schema([], [{"id": "c1", "text": "x"}])
    assert "instruments" not in schema["properties"]
    assert schema["required"] == ["possibleRisk", "explanation", "recommendedAction", "sources"]
    prompt = build_user_prompt({"stepId": 39}, [], [], {})
    assert "BEKLENEN ALETLER: yok" in prompt
