# VR geliştiricisi için kullanım kılavuzu

Kısa kural: **VR yalnızca "ne oldu" bilgisini gönderir. Doğru mu yanlış mı, puan, sıradaki adım: hepsini backend söyler.** VR kendi başına karar vermez.

Ayrıntılı alan listesi: [vr-events.md](vr-events.md). Bu sayfa adım adım ne yapılacağını anlatır.

**İlk kez başlıyorsan önce [vr-ilk-test.md](vr-ilk-test.md):** giriş yap, seans aç, ilk aleti gönder, cevabı gör (terminalden ya da hazır Unity betiğiyle).

---

## 0. Adresler

| Ne | Adres |
|---|---|
| **Swagger (API belgesi, deneme ekranı)** | https://setaceous-shriekingly-soo.ngrok-free.dev/swagger |
| Backend (internetten) | https://setaceous-shriekingly-soo.ngrok-free.dev |
| Backend (ev/ofis ağından) | http://192.168.1.60:5080 |
| Swagger (ağdan) | http://192.168.1.60:5080/swagger |
| Web simülatörü (aynı akışı ekranda görmek için) | https://arqenion-sim.vercel.app |
| **VR Test ekranı (gözlükten gelen istekleri canlı izle)** | https://arqenion-sim.vercel.app/vr.html |
| Kendi bilgisayarında backend | http://localhost:5080/swagger |

- ngrok adresine koddan istek atarken her isteğe şu başlığı ekle: `ngrok-skip-browser-warning: 1`. Yoksa JSON yerine ngrok uyarı sayfası gelir.
- Her isteğe cihaz adını da ekle: `X-Arqenion-Device: Quest 3 · Ali`. VR Test ekranında cihaz bu adla görünür; yoksa "Unity" yazar.
- Swagger sayfası giriş istemez; içindeki uçlar giriş ister (1. adım).

---

## 1. Hesap al ve giriş yap

1. Eğitmen (Mennan) sana bir kullanıcı açar: simülatörde eğitmen girişi → **Eğitmen** ekranı → kullanıcı aç. Kullanıcı adı ve şifre bir kez gösterilir, sakla.
2. Giriş isteği:

   ```http
   POST /api/auth/login
   { "username": "vr-gelistirici", "password": "..." }
   ```

   Cevap: `{ "token": "eyJ...", "expiresAt": "...", "user": { ... } }`

3. Bundan sonraki **her istekte** başlık: `Authorization: Bearer <token>`
4. Jeton **8 saat** geçerli. 401 gelirse yeniden giriş yap.

**Swagger'da:** `POST /api/auth/login` → *Try it out* → kullanıcı adı/şifre → *Execute*. Gelen `token`'ı kopyala, sayfanın üstündeki **Authorize** düğmesine yapıştır. Artık diğer uçları Swagger'dan deneyebilirsin.

---

## 2. Aletleri ve modelleri yükle (uygulama açılışında bir kez)

1. `GET /api/procedure` → `instrumentCatalog` (49 alet) ve `sutureCatalog` (iplikler). **Tepsiyi bu listeyle kur.** Her alette `code` var; olaylarda bu kod gönderilir.
   - Listede "yanıltıcı" aletler de var (ör. 11 numara bistüri). Bunları da tepsiye koy; kursiyer yanlışlıkla seçebilmeli.
2. `GET /api/assets/manifest` → 3D modellerin listesi.
   - `manifestHash` cihazdakiyle aynıysa hiçbir şey indirme.
   - Değilse her öğenin `sha256`'sını kontrol et, farklı olanı `url`'den indir (`.glb`).
   - `missing` listesindeki aletlerin modeli henüz yok: yer tutucu (kutu) göster.
   - **Şu an gerçek model yok.** Listede yalnızca 5 örnek dosya var (ör. `NEEDLE_HOLDER_DEBAKEY_18.glb`). Bunlar aletin boyunda renkli kutulardır ve indirme ile güncelleme akışını denemek için konmuştur. Gerçek modeller aynı adla konunca VR yalnızca değişen dosyaları indirir.

---

## 3. Seansı başlat

```http
POST /api/sessions
{ "scenarioId": "case-basic-01" }
```

Senaryolar:

| `scenarioId` | Ne |
|---|---|
| `case-basic-01` | Standart vaka (başlangıç için bunu kullan) |
| `case-adhesion-01` | Yapışıklık dalı |
| `case-bleeding-01` | Kanama dalı |
| `case-count-01` | Sayım uyuşmazlığı |
| `case-obese-01` | Kilolu hasta |
| `case-full-01` | Ameliyat öncesi kontrol listeleri dahil tam vaka |

Cevaptan sakla:
- `sessionId` → sonraki bütün isteklerde lazım.
- `currentStep` → kursiyere şu an ne gösterilecek (5. adıma bak).

---

## 4. Canlı bildirimlere bağlan (SignalR)

1. Bağlan: `/hubs/simulation?access_token=<token>`
2. Bağlanınca çağır: `JoinSession(sessionId)`
3. Dinle:
   - `ScoreUpdate` → `{ sessionId, score }`: puanı güncelle.
   - `AiFeedback` → yapay zekânın hata açıklaması. Hatadan **birkaç saniye sonra** gelir, bazen hiç gelmez. Gelmezse sorun değil (6. adımdaki `complication` yeterli).

Unity için: Microsoft'un SignalR .NET istemcisi (`Microsoft.AspNetCore.SignalR.Client`) kullanılabilir.

---

## 5. Adımı ekranda göster

`currentStep` (ve her cevaptaki `nextStep`) içinde:

| Alan | VR'da ne yapılır |
|---|---|
| `title`, `instruction` | Görev metni olarak yaz |
| `surgeonRequest` | Doluysa cerrahın sesi/altyazısı: "Bana ... ver" |
| `kind` | Kursiyerin ne yapacağı (aşağıdaki tablo) |
| `checklist` | Yalnız ameliyat öncesi adımlarda: işaretlenecek maddeler |
| `countCategory`, `tableCount` | Sayım adımında neyin sayılacağı |

| `kind` | Kursiyer ne yapar | Gönderilecek olay |
|---|---|---|
| `delivery` | Cerraha alet/iplik uzatır | `instrument_delivered` |
| `confirm` | Cerrahı izler, alet vermez, "tamam" der | `step_confirmed` |
| `count` | Gazlı bez/alet sayar | `count_confirmed` |
| `checklist` | Listeyi işaretler | `checklist_confirmed` |

> **Önemli:** `allowedInstruments` alanı doğru cevabı içerir. Bunu kursiyere **gösterme**; yalnız hata ayıklamada kullan.

---

## 6. Kursiyerin her hareketini gönder

```http
POST /api/sessions/{sessionId}/events
```

**Alet uzattı:**
```json
{ "eventId": "yeni-uuid", "eventType": "instrument_delivered",
  "instrumentCode": "SCALPEL_BLADE_15", "target": "SURGEON",
  "timestamp": "2026-10-22T09:00:00.000Z" }
```

**İplikli portegü uzattı:** `instrumentCode` + `"sutureCode": "SUTURE_POLYGLACTIN_0"`

**Adımı onayladı:**
```json
{ "eventId": "yeni-uuid", "eventType": "step_confirmed" }
```

**Sayım:**
```json
{ "eventId": "yeni-uuid", "eventType": "count_confirmed", "counts": { "COMPRESS": 9 } }
```

**Kontrol listesi:**
```json
{ "eventId": "yeni-uuid", "eventType": "checklist_confirmed", "checkedItems": ["PRE01.1", "PRE01.2"] }
```

**Her an olabilecekler:** `hint_requested` (ipucu, −2 puan), `sterile_area_violated` (steril alan ihlali), `instrument_dropped` (alet düştü).

Kurallar:
- Her olaya **yeni bir UUID** ver (`eventId`).
- Ağ koptu, cevap gelmedi → **aynı `eventId`** ile tekrar gönder. Backend ikinci kez saymaz.
- **Sırayla gönder:** cevap gelmeden sıradaki olayı gönderme.

---

## 7. Cevabı işle

Cevap hemen gelir (100 ms altı):

| Alan | VR'da ne yapılır |
|---|---|
| `success: true` | Doğru. `nextStep`'i göster (5. adıma dön). |
| `success: false` | Yanlış. `message`'ı göster. `allowRetry: true` ise kursiyer aynı adımı yeniden dener. |
| `deviation` | Hatanın türü (`WRONG_INSTRUMENT`, `WRONG_ORDER`, `COUNT_MISMATCH`...) |
| `complication` | Hatanın klinik açıklaması: `possibleRisk`, `explanation`, `recommendedAction`. Hemen göster. |
| `score` | Güncel puan (100'den başlar, hatada düşer) |
| `branch` | Doluysa akış bir dala girdi/çıktı (ör. kanama). `nextStep` zaten doğru adımı verir; ayrıca bir şey yapma, istersen bilgi göster. |
| `completed: true` | Ameliyat bitti → 8. adım |

Dallar (kanama, yapışıklık, sayım uyuşmazlığı) için VR'da **özel kod yazma**: her zaman `nextStep` neyse onu göster.

---

## 8. Bitir ve raporu al

```http
POST /api/sessions/{sessionId}/complete
```

Rapor gelir: son puan, hatalar, girilen dallar. Sonra SignalR bağlantısını kapat.

---

## 9. Hata kodları

| Kod | Anlamı | Ne yap |
|---|---|---|
| 200 + `success: false` | **Kursiyer** hata yaptı | Normal; göster, devam et |
| 400 | **VR kodu** hatalı (bilinmeyen alet kodu, eksik alan, yanlış olay adı) | Cevaptaki `message` ve `field`'a bak, kodu düzelt. Puana işlemez. |
| 401 | Jeton yok ya da süresi doldu | Yeniden giriş (1. adım) |
| 404 | Seans yok ya da başkasının | `sessionId`'yi kontrol et |
| 429 | Çok fazla giriş denemesi (dakikada 10) | Biraz bekle |

---

## 10. Kendi kodunu nasıl denersin

1. **Swagger'dan elle:** 1 → 3 → 6 adımlarını Swagger'da yap, cevapları gör.
2. **Web simülatöründe:** https://arqenion-sim.vercel.app aynı uçları kullanır. Aynı senaryoyu orada oynayıp hangi adımda hangi aletin doğru olduğunu görebilirsin.
3. **Hazır kayıtlar:** `backend.Tests/VrSessions/` klasöründe baştan sona örnek oturumlar var (gönderilen her JSON ve beklenen cevap). En kolayı `standart.json`: hatasız 56 adım. VR'ın göndermesi gereken olaylar birebir bunlar.
4. **Bir kaydı sunucuya oynat** (giriş bilgisi `--user/--pass` ile):
   ```bash
   node scripts/vr-replay.mjs replay backend.Tests/VrSessions/standart.json --url http://192.168.1.60:5080 --user vr-gelistirici --pass '...'
   ```

---

## Örnek: ilk alet (baştan sona)

1. `POST /api/auth/login` → `token` al.
2. `POST /api/sessions` gövde `{ "scenarioId": "case-basic-01" }` → cevapta `sessionId` ve `currentStep`.
   (`userId` gönderilse de yok sayılır; seans giriş yapan kullanıcınındır.)
3. `currentStep` 1. adımdır: "Sağ alt kadranda McBurney cilt insizyonu", `kind: "delivery"`.
   `surgeonRequest` cerrahın istediği bistüriyi söyler (No:10 ya da No:15, seanstan seansa değişir). VR bunu cerrahın sesi olarak gösterir.
4. Kursiyer bistüriyi alıp cerraha uzattı → `POST /api/sessions/{sessionId}/events`:
   ```json
   { "eventId": "b1f2...-yeni-uuid", "eventType": "instrument_delivered",
     "instrumentCode": "SCALPEL_BLADE_15", "target": "SURGEON" }
   ```
5. Cevap:
   - İstenen bistüri → `success: true`, `score: 100`, `nextStep` = 2. adım (koter). VR 2. adımı gösterir.
   - Başka bistüri (ör. `SCALPEL_BLADE_11`) → `success: false`, puan düşer, `message` ve `complication` gösterilir, `nextStep` yine 1. adım: kursiyer doğru aleti yeniden dener.

Kalan bütün adımlar aynı döngüdür.

---

## Kısa özet

1. Giriş yap → jeton al.
2. Katalog + modelleri indir.
3. Seans aç → `sessionId`.
4. SignalR'a bağlan → `JoinSession`.
5. `currentStep`'i göster.
6. Kursiyer bir şey yaptı → olayı gönder.
7. Cevaptaki `nextStep`'i göster, hata varsa `message` + `complication`'ı göster. 5'e dön.
8. `completed: true` → `complete` çağır, raporu göster.
