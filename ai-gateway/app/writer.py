"""Yaz adımı: yapay zekâya yalnızca bulunan kaynakları verip kısa bir açıklama yazdırır.

Yazıcı karar vermez. Ne yazarsa yazsın, sonraki Kontrol adımı düz kodla denetler.
"""
import json

SYSTEM_PROMPT = """Sen ameliyathane teknikeri (scrub) eğitiminde bir eğitmensin.
Kursiyer bir hata yaptı; hatanın ne olduğuna motor zaten karar verdi. Senin işin bu hatayı kursiyere kısaca anlatmak.

Kurallar:
- Yalnızca KAYNAKLAR bölümündeki bilgiyi kullan. Kaynakta olmayan bilgi, alet, sayı ya da süre ekleme.
- Türkçe yaz. Her alan en fazla 2 kısa cümle olsun.
- Doğru alet önerirken yalnızca BEKLENEN ALETLER listesindeki kodları, harfi harfine aynen kullan.
- BEKLENEN ALETLER "yok" ise bu adımda alet teslimi yoktur: instruments boş liste olsun ([]); faz, adım ya da kaynak kodlarını alet diye yazma.
- Cevabı yalnızca şu JSON biçiminde ver, başka hiçbir şey yazma:
{"possibleRisk": "...", "explanation": "...", "recommendedAction": "...", "instruments": ["<alet kodu>"], "sources": ["<kaynak kimliği>"]}
- sources: kullandığın kaynakların kimlikleri (KAYNAKLAR bölümündeki köşeli parantez içindeki kimlikler)."""


def build_user_prompt(deviation: dict, expected: list, chunks: list, names: dict) -> str:
    def label(code):
        return f"{code} ({names.get(code, code)})" if code else "-"

    lines = [
        f"ADIM: {deviation.get('stepId')}",
        f"SAPMA TÜRÜ: {deviation.get('deviationType', '-')}",
        f"BEKLENEN: {label(deviation.get('expected'))}",
        f"VERİLEN: {label(deviation.get('actual'))}",
        "BEKLENEN ALETLER: " + (", ".join(label(c) for c in expected) if expected
                                else "yok (bu adımda alet teslimi yok; instruments boş liste olmalı)"),
        "",
        "KAYNAKLAR:",
    ]
    for c in chunks:
        lines.append(f"[{c['id']}]")
        lines.append(c["text"][:2500])
        lines.append("")
    return "\n".join(lines)


def build_schema(expected: list, chunks: list) -> dict:
    """Yapılandırılmış çıktı şeması: alet ve kaynak kodları yalnızca verilen listelerden seçilebilir.

    Böylece kesik ya da uydurma kod yazılamaz. Beklenen alet yoksa instruments alanı şemada hiç yer almaz.
    Kontrol adımı yine de her şeyi düz kodla denetler; şema yalnızca hatayı baştan önler.
    """
    text = {"type": "string"}
    properties = {"possibleRisk": text, "explanation": text, "recommendedAction": text}
    if expected:
        properties["instruments"] = {"type": "array", "items": {"type": "string", "enum": list(expected)}}
    ids = [c["id"] for c in chunks]
    properties["sources"] = {"type": "array", "items": {"type": "string", "enum": ids} if ids else text}
    return {"type": "object", "properties": properties, "required": list(properties), "additionalProperties": False}


class OpenAIWriter:
    mode = "openai"

    def __init__(self, api_key: str, model: str, timeout: float, base_url: str = ""):
        from openai import OpenAI
        self.model = model
        self.client = OpenAI(api_key=api_key, timeout=timeout, max_retries=0, base_url=base_url or None)

    def write(self, deviation, expected, chunks, names) -> dict:
        resp = self.client.chat.completions.create(
            model=self.model,
            temperature=0.2,
            response_format={"type": "json_schema", "json_schema": {
                "name": "feedback", "strict": True, "schema": build_schema(expected, chunks)}},
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": build_user_prompt(deviation, expected, chunks, names)},
            ],
        )
        draft = json.loads(resp.choices[0].message.content)
        draft.setdefault("instruments", [])
        usage = getattr(resp, "usage", None)
        draft["_meta"] = {
            "model": self.model,
            "promptTokens": getattr(usage, "prompt_tokens", None),
            "completionTokens": getattr(usage, "completion_tokens", None),
        }
        return draft


def make_writer(settings):
    """Anahtar yoksa yazıcı yok: akış her seferinde hazır klinik metne düşer."""
    if not settings.openai_api_key:
        return None
    return OpenAIWriter(settings.openai_api_key, settings.openai_model, settings.ai_timeout_sec, settings.openai_base_url)
