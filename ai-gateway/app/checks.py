"""Kontrol adımı: yapay zekânın taslağını DÜZ KODLA denetler. Bir sorun bile varsa taslak gönderilmez."""

REQUIRED_TEXT = ("possibleRisk", "explanation", "recommendedAction")
MAX_CHARS = 600


def check_draft(draft, chunk_ids, expected, catalog, actual=None) -> list:
    if not isinstance(draft, dict):
        return ["taslak yok"]
    problems = []
    for key in REQUIRED_TEXT:
        value = draft.get(key)
        if not isinstance(value, str) or not value.strip():
            problems.append(f"{key} boş")
        elif len(value) > MAX_CHARS:
            problems.append(f"{key} çok uzun")
    sources = draft.get("sources")
    if not isinstance(sources, list) or not sources:
        problems.append("kaynak gösterilmemiş")
    else:
        unknown = [s for s in sources if s not in chunk_ids]
        if unknown:
            problems.append(f"bulunmayan kaynak: {', '.join(map(str, unknown))}")
    instruments = draft.get("instruments", [])
    if not isinstance(instruments, list):
        problems.append("instruments liste değil")
        instruments = []
    for code in instruments:
        if code not in catalog:
            problems.append(f"katalogda olmayan alet: {code}")
        elif code not in expected:
            problems.append(f"bu adımda beklenmeyen alet: {code}")
        if actual and code == actual:
            problems.append(f"yanlış verilen alet doğru diye önerilmiş: {code}")
    return problems
