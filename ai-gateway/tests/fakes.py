"""Testler için sahte yazıcılar: gerçek yapay zekâ olmadan akışın iki yolunu da denemeye yarar."""


class GoodWriter:
    """Kurallara uyan bir yapay zekâ gibi davranır: kaynak gösterir, yalnızca beklenen aleti önerir."""
    mode = "fake-good"

    def write(self, deviation, expected, chunks, names):
        wanted = deviation.get("expected")
        return {
            "possibleRisk": "Kaynaktaki komplikasyon.",
            "explanation": chunks[0]["text"].splitlines()[0][:300],
            "recommendedAction": f"{names.get(wanted, 'Beklenen alet')} hazırlanır.",
            "instruments": [wanted] if wanted in expected else [],
            "sources": [chunks[0]["id"]],
        }


class WrongInstrumentWriter(GoodWriter):
    """Yanlış verilen aleti doğruymuş gibi öneren bir yapay zekâ (kontrolün yakalaması gerekir)."""
    mode = "fake-wrong-instrument"

    def write(self, deviation, expected, chunks, names):
        draft = super().write(deviation, expected, chunks, names)
        draft["instruments"] = [deviation["actual"]]
        return draft


class MadeUpSourceWriter(GoodWriter):
    """Bulunmayan bir kaynağa dayanan bir yapay zekâ (kontrolün yakalaması gerekir)."""
    mode = "fake-made-up-source"

    def write(self, deviation, expected, chunks, names):
        draft = super().write(deviation, expected, chunks, names)
        draft["sources"] = ["uydurma-kaynak"]
        return draft


class BrokenWriter:
    """Zaman aşımı ya da ağ hatası veren bir yapay zekâ."""
    mode = "fake-broken"

    def write(self, *args):
        raise TimeoutError("yapay zekâ 3 sn içinde cevap vermedi")
