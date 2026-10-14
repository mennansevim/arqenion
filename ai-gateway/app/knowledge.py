"""Akış modeline erişim: adımlar, hata senaryoları, alet kataloğu, RAG parçaları ve akış motoru.

Bul adımı vektör araması kullanmaz: motor adım numarasını (ve varsa senaryo kimliğini) verir,
doğru parçalar tam eşleşmeyle bulunur. Senaryo kimliği yoksa, verilen yanlış aletin adı
o adımın senaryo başlıklarında aranır.
"""
import json
import re
import sys
from pathlib import Path

INACTIVE = {"SUPERSEDED", "DRAFT"}
STOPWORDS = {"iğneli", "cm", "no", "ile", "ve", "veya", "için", "set", "seti", "adet"}
# Genel başlıklarda ("yanlış sütür", "farklı tipte klemp") verilen alet adıyla değil kategorisiyle anılır.
CATEGORY_WORDS = {"suture": {"sütür", "iplik"}, "clamp": {"klemp"}, "forceps": {"penset", "pens"}, "scissors": {"makas"},
                  "blade": {"bistüri"}, "retractor": {"ekartör"}, "antiseptic": {"solüsyon"}, "fluid": {"solüsyon"}}
GENERIC_WORDS = {"dişli", "steril", "radyopak", "yıkama", "izotonik", "kullanılmış", "hasta", "çelik", "klorheksidinli"}
CHOICE_MARKERS = (" yerine ", " istendiği hâlde ", " istendiği halde ", "gerekmediği hâlde", "gerekmediği halde", "farklı")
PARAM_WORDS = {"AESTHETIC": "estetik", "STANDARD": "standart", "OBESE": "kilolu"}


def _norm(text: str) -> str:
    return text.replace("İ", "i").replace("I", "ı").lower()


def _tokens(text: str) -> set:
    """Anlamlı kelimeler + iplik numaraları (3/0, 4/0)."""
    t = _norm(text)
    words = {w for w in re.findall(r"[a-zçğıöşü]+", t) if len(w) > 2 and w not in STOPWORDS}
    return words | set(re.findall(r"\d/0", t))


class Knowledge:
    def __init__(self, model_dir: Path, version: str, engine_dir: Path):
        base = Path(model_dir) / f"open-appendectomy-v{version}"
        self.model = json.loads(Path(f"{base}.json").read_text(encoding="utf-8"))
        self.version = self.model["schemaVersion"]
        self.chunks = {}
        for line in Path(f"{base}.chunks.jsonl").read_text(encoding="utf-8").splitlines():
            c = json.loads(line)
            self.chunks[c["id"]] = c
        if str(engine_dir) not in sys.path:
            sys.path.insert(0, str(engine_dir))
        from flow_engine import FlowEngine  # procedure-model/tools/flow_engine.py
        self.engine = FlowEngine(f"{base}.graph.json")

        self.instruments = self.model["catalogs"]["instruments"]
        self.descriptors = {}   # alet adında geçmeyen niteleyiciler (ör. Ochsner-Kocher → "dişli"); backend ile aynı dosyadan
        self.steps = {s["step"]: s for s in self.model["steps"]}
        self.scenarios = {}                      # id -> (adım, senaryo)
        for s in self.model["steps"]:
            for e in s["scrub"]["errorScenarios"]:
                if e["reviewStatus"] not in INACTIVE:
                    self.scenarios[e["id"]] = (s["step"], e)
        prefix = f"open-appendectomy@{self.version}"
        self._step_chunk = lambda n: f"{prefix}:step:{n}"
        self._scenario_chunk = lambda sid: f"{prefix}:error:{sid}"
        self._instrument_chunk = lambda code: f"{prefix}:instrument:{code}"

    # --- motor ---------------------------------------------------------------
    def expected_instruments(self, step, parameters=None) -> list:
        """Bu adımda kabul edilen aletler (motorun kararı + adımın alet listesi, parametreye göre süzülmüş)."""
        step_def = self.steps.get(step)
        if step_def is None:
            return []
        out = self.engine.expected_instruments(step, parameters or {})
        codes = set(out.get("accepted", [])) | {i["code"] for i in step_def["instruments"] if i["usage"] != "STANDBY"}
        by_param = out.get("byParameter")
        if by_param:
            rule = next(r for r in step_def.get("parameterRules", []) if r["parameter"] == by_param["parameter"])
            all_param = {c for v in rule["expectedInstruments"].values() for c in v}
            codes -= all_param - set(by_param["instruments"])
        return sorted(codes)

    def wanted_instruments(self, step, deviation_expected=None, parameters=None) -> list:
        """Kursiyerin vermesi gereken: motor söylediyse o, yoksa parametre kuralının beklediği."""
        if deviation_expected:
            return [deviation_expected]
        out = self.engine.expected_instruments(step, parameters or {}) if step in self.steps else {}
        return list((out.get("byParameter") or {}).get("instruments", []))

    def instrument_name(self, code) -> str:
        return self.instruments.get(code, {}).get("name", code or "")

    # --- bul -----------------------------------------------------------------
    def words_of(self, code) -> set:
        inst = self.instruments.get(code) or {}
        return (_tokens(inst.get("name", "")) | _tokens(inst.get("material", "")) | _tokens(inst.get("referenceName", ""))
                | set(self.descriptors.get(code, [])))

    def brands(self) -> set:
        """Alet "markaları": katalog adlarının ilk kelimesi (crile, allis, babcock…). Başlık başka bir aleti anıyorsa ceza."""
        out = set()
        for inst in self.instruments.values():
            m = re.search(r"[a-zçğıöşü]{4,}", _norm(inst.get("name", "")))
            if m and m.group() not in STOPWORDS and m.group() not in GENERIC_WORDS:
                out.add(m.group())
        return out

    def match_scenario(self, step, actual=None, wanted=None, parameters=None):
        """Senaryo kimliği verilmediyse: verilen yanlış alet ve beklenen alet hangi senaryonun başlığına uyuyor?

        Başlıkta yanlış aletin sağda, beklenenin solda geçmesi puan kazandırır; hasta parametresi
        (ör. estetik kapatma) başlıkta geçerse ek puan. Hiç eşleşme yoksa None.
        """
        if not actual or actual not in self.instruments:
            return None
        bad, good = self.words_of(actual), set()
        kind = CATEGORY_WORDS.get(self.instruments[actual].get("category"), set())
        brands = self.brands()
        for code in wanted or []:
            good |= self.words_of(code)
        context = {PARAM_WORDS[v] for v in (parameters or {}).values() if v in PARAM_WORDS}
        best, best_score = None, 0
        for sid, (n, e) in self.scenarios.items():
            if n != step:
                continue
            # Başlık kalıbı: "<beklenen> yerine <verilen>" ya da "<beklenen> istendiği hâlde <verilen>"
            title = _norm(e["title"])
            if not any(m in title for m in CHOICE_MARKERS):   # montaj/kontrol hataları yanlış seçime bağlanmaz
                continue
            left, right = "", title
            for sep in (" yerine ", " istendiği hâlde ", " istendiği halde "):
                if sep in title:
                    left, right = title.split(sep, 1)
                    break
            lw, rw, body = _tokens(left), _tokens(right), _tokens(e["scrubError"])
            if not ((bad | kind) & rw):   # uzatılan alet (adı ya da türü) başlığın "verilen" tarafında geçmeli
                continue
            score = 3 * len(bad & rw) + 2 * len(good & lw) - 2 * len(bad & lw) + len(bad & body)
            score -= 4 * len({w for w in rw if w in brands and w not in bad})
            sizes = {w for w in rw if "/" in w}
            if sizes and not sizes & bad:            # başlıktaki numara verilen ipliğin numarası değil
                score -= 3
            # Başlıkta verilen aletin numarası geçiyorsa (11 numaralı bistüri, 15 cm Allis) uzatılanınkiyle uyuşmalı.
            title_numbers = set(re.findall(r"\d+/\d+|\d+", right))
            if title_numbers and not title_numbers & set(re.findall(r"\d+/\d+|\d+", self.instrument_name(actual))):
                continue
            if title_numbers:
                score += 3
            score += 2 * len(context & _tokens(title))
            if score > best_score:
                best, best_score = sid, score
        return best

    def find(self, step, scenario_id=None, actual=None, expected=None, wanted=None, parameters=None):
        """Dönüş: (senaryo kimliği ya da None, parça listesi).

        wanted: kursiyerin vermesi gereken alet(ler) — motorun 'expected' alanı ya da parametreye göre beklenenler.
        """
        if scenario_id not in self.scenarios or self.scenarios[scenario_id][0] != step:
            scenario_id = self.match_scenario(step, actual, wanted, parameters)
        ids = []
        if scenario_id:
            ids.append(self._scenario_chunk(scenario_id))
        ids.append(self._step_chunk(step))
        for code in [actual] + list(expected or []):
            if code and self.instruments.get(code, {}).get("category") == "suture":
                ids.append(self._instrument_chunk(code))
        seen, chunks = set(), []
        for cid in ids:
            if cid in self.chunks and cid not in seen:
                seen.add(cid)
                chunks.append(self.chunks[cid])
        return scenario_id, chunks[:6]

    def scenario(self, scenario_id):
        return self.scenarios.get(scenario_id, (None, None))[1]
