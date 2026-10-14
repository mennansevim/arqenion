# Arqenion — Yol Haritası (açık apendektomi)

Güncel ve ayrıntılı sürüm (AI akışı çizimi ve takvimle): https://claude.ai/code/artifact/23893c77-41ab-4a96-840f-e86dbc3bac10
Bu dosya kısa özettir; ikisi ayrışırsa doküman esastır.

**İlke:** Kararı motor verir, AI anlatır. Prototipte motor `tools/flow_engine.py`, üretimde C# backend.

| # | İş | Süre | Bitti sayılır |
|---|---|---|---|
| 1 | AI akışını çiz (LangGraph: Bul → Yaz → Denetle → Gönder, geçmezse klinik tablo metni) | 1–2 gün | Düğüm listesi ve durum şeması kesin |
| 2 | Vektör veritabanı (pgvector + `chunks.jsonl` yükleyici + 40 soruluk test seti) | ≈1 hafta | Doğru parça ilk 5'te ≥ %90 |
| 3 | AI prototipi (FastAPI + LangGraph, motor = `flow_engine.py`) | ≈2 hafta | Denetimden geçen cevap ≥ %95, yanlış alet önerisi 0, 3 sn'de cevap yoksa klinik tablo |
| 4 | Backend'i 60 adıma geçir (graph.json okuyucu, C# yol bulma, golden testler, `scoring.json`) — 1–3 ile paralel | ≈2 hafta | Simülatörde 60 adım oynanıyor, golden testler geçiyor |
| 5 | VR olay sözleşmesi (`docs/vr-events.md`) — paralel | ≈1 hafta | Simülatör aynı sözleşmeyle konuşuyor |
| 6 | Bağla: `AiFeedbackService` → AI servisi → SignalR `AiFeedback` | ≈1 hafta | Hata yapınca 3 sn içinde açıklama ekranda |
| 7 | Adaptif motor (7 adaptif koşul, kural tabanlı; sınav modunda sabit) | ≈2 hafta | Her seçim kayıtlı |
| 8 | Klinik doğrulama ve pilot (5 teyitsiz karar: Q-B1, Q-B2, Q-B4, Q-B5, Q-D1) | — | Fatih'in onayı + 5–10 kursiyerle pilot |

Tek kişi çalışılıyorsa sıra 1 → 2 → 3 → 4 → 5 → 6; takvim yaklaşık 3 hafta uzar.

**LangGraph mi, ADK mi?** LangGraph: akış sabit bir hat, geri dönüş kuralı grafta durur, modelden bağımsız. ADK, tamamen Google Cloud/Gemini'ye geçilecekse ya da çoklu ajan gerekirse. Düğümler düz Python fonksiyonu olacağı için karar geri alınabilir.
