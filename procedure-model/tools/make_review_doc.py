# -*- coding: utf-8 -*-
"""Model → klinik inceleme belgesi (taslak senaryolar + mevcut senaryoların durumu)."""
import json, sys
m = json.load(open(sys.argv[1])); out = open(sys.argv[2], "w")
S = {s["step"]: s for s in m["steps"]}
w = lambda t="": out.write(t + "\n")
STAT = {"ACTIVE": "Aynen kalıyor", "ADAPTED": "Metni yeni karara göre uyarlandı", "SUPERSEDED": "Devreden çıktı",
        "CONDITIONAL": "Koşullu", "DRAFT": "YENİ TASLAK"}
w("# Adım 50, 52 ve 56: Hata Senaryoları İncelemesi"); w()
w("Hocam, iplik kararlarınız değiştiği için bu üç adımın hata senaryolarını yeniden düzenledim. Her adımda önce mevcut senaryoların durumunu, sonra yeni taslakları göreceksiniz.")
w(); w("Her taslağın altına **Onay**, **Düzeltme: …** ya da **Gerek yok** yazmanız yeterli. Onaylanan taslak yerini aldığı eski senaryonun yerine geçer. Emilme süreleri gönderdiğiniz tablodan alındı.")
for n in (50, 52, 56):
    s = S[n]
    w(); w(f"## Adım {n}: {s['name']}"); w()
    w(f"**Doğru uygulama (modeldeki hali):** {s['scrub']['correctPractice']}"); w()
    w("**Mevcut senaryolar:**"); w()
    for e in s["scrub"]["errorScenarios"]:
        if e["reviewStatus"] == "DRAFT": continue
        note = e.get("review", {}).get("note", "")
        w(f"- {e['id']} · {e['title']} → {STAT[e['reviewStatus']]}" + (f" ({note})" if note else ""))
    for e in s["scrub"]["errorScenarios"]:
        if e["reviewStatus"] != "DRAFT": continue
        rep = e["review"].get("replaces")
        w(); w(f"### {e['id']} · {e['title']}" + (f" (yerini aldığı: {rep})" if rep else " (yeni)")); w()
        w(f"**Scrub'ın yaptığı yanlışlık:** {e['scrubError']}"); w()
        for key, lbl in (("possibleComplications", "Olası komplikasyon"), ("signs", "Komplikasyon belirtileri")):
            w(f"**{lbl}:**"); [w(f"- {x}") for x in e[key]]; w()
        w("**Scrub'ın sıralı çözüm yaklaşımı:**"); [w(f"{i + 1}. {x}") for i, x in enumerate(e["resolutionSteps"])]; w()
        w(f"**Vakanın devamı:** {e['caseContinuation']}"); w()
        w("**Yönetim ve organizasyona etkisi:**"); [w(f"- {x}") for x in e["organizationalImpact"]]; w()
        w("> **Onay / Düzeltme / Gerek yok:** ")
w(); w("## Cevap bekleyen önceki sorular"); w()
for q in m["openQuestions"]:
    if q["id"].startswith("Q-B"):
        w(f"- **{q['id']} (Adım {q['step']}):** {q['question']}")
