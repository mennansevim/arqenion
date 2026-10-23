# İlk test: bağlan, başlat, ilk olayı gönder

Amaç: backend'e bağlanıp bir seans başlatmak, ilk aleti göndermek ve cevabı görmek. Başka hiçbir şey yok.

**Önce VR Test ekranını aç:** https://arqenion-sim.vercel.app/vr.html (aynı ağdaysan http://192.168.1.60:5080/sim/vr.html). Aynı hesapla giriş yap. Gözlükten (ya da terminalden) gelen her istek orada canlı görünür: hangi adım geçti, hangisi bekleniyor, hata varsa neden. Kumanda test ekranı gibi adım adım yönlendirir.

İki yol var, ikisi de aynı üç isteği atar:
- **A. Terminalden (curl):** 5 dakika, Unity gerekmez. Önce bunu yap.
- **B. Unity'den:** hazır betik `docs/vr-ornek/ArqenionIlkTest.cs`.

Gerekenler:
- Eğitmenin açtığı kullanıcı adı ve şifre.
- Sunucu adresi: `https://setaceous-shriekingly-soo.ngrok-free.dev`. Aynı ağdaysan `http://192.168.1.60:5080` da olur.

---

## A. Terminalden

Aşağıdaki komutları sırayla terminale yapıştır.

### 1) Adresi ve hesabı ayarla

```bash
export ARQ=https://setaceous-shriekingly-soo.ngrok-free.dev
export ARQ_USER=vr-gelistirici
export ARQ_PASS='şifren'
```

### 2) Giriş yap, jetonu al

```bash
curl -s $ARQ/api/auth/login -H 'ngrok-skip-browser-warning: 1' -H 'Content-Type: application/json' -d "{\"username\":\"$ARQ_USER\",\"password\":\"$ARQ_PASS\"}"
```

Cevap:
```json
{ "token": "eyJhbGciOi...", "expiresAt": "...", "user": { "username": "vr-gelistirici", "role": "trainee" } }
```

`token` değerini tırnaksız kopyala ve kaydet:

```bash
export TOKEN='eyJhbGciOi...'
```

### 3) Seansı başlat

```bash
curl -s $ARQ/api/sessions -H 'ngrok-skip-browser-warning: 1' -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"scenarioId":"case-basic-01","seed":"ilk-test"}'
```

Cevap (kısaltılmış):
```json
{
  "sessionId": "660e983c-054b-43b0-91b2-26b4eb6fa1e9",
  "currentStep": {
    "nodeId": "1",
    "kind": "delivery",
    "title": "Sağ alt kadranda McBurney (veya Elliot) cilt insizyonu",
    "surgeonRequest": "No:10 bistüri ucu",
    "instruction": "Cerrah istiyor: No:10 bistüri ucu. Cerraha teslim edin: No:10 bistüri ucu",
    "allowedInstruments": ["SCALPEL_BLADE_10"]
  },
  "score": 100
}
```

- `seed: "ilk-test"` sayesinde cerrah bu testte hep **No:10** bistüri ister. Seed gönderilmezse seanstan seansa değişir; gerçek derste seed gönderilmez.
- `sessionId` değerini kaydet:

```bash
export SID='660e983c-...'
```

### 4) İlk olayı gönder: kursiyer bistüriyi cerraha uzattı

```bash
curl -s $ARQ/api/sessions/$SID/events -H 'ngrok-skip-browser-warning: 1' -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"eventId":"ilk-test-001","eventType":"instrument_delivered","instrumentCode":"SCALPEL_BLADE_10","target":"SURGEON"}'
```

Doğru alet. Cevap (kısaltılmış):
```json
{
  "success": true,
  "message": "Doğru işlem.",
  "score": 100,
  "currentNodeId": "1",
  "nextNodeId": "2",
  "nextStep": {
    "title": "Cilt altı yağ dokusunun diseksiyonu",
    "instruction": "Cerraha teslim edin: Monopolar elektrokoter kalemi (düz uç)"
  }
}
```

VR bundan sonra `nextStep`'i ekrana getirir. İlk test bu kadar. ✅

### 5) (İsteğe bağlı) Yanlış aleti dene

Yeni bir seans aç (3. adım), bu kez 15 numara bistüriyi gönder:

```bash
curl -s $ARQ/api/sessions/$SID/events -H 'ngrok-skip-browser-warning: 1' -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"eventId":"ilk-test-002","eventType":"instrument_delivered","instrumentCode":"SCALPEL_BLADE_15","target":"SURGEON"}'
```

Cevap:
```json
{
  "success": false,
  "score": 92,
  "message": "Cerrah No:10 bistüri ucu istedi; istem doğrulanmadan farklı bir alternatif uzatıldı.",
  "deviation": { "deviationType": "WRONG_INSTRUMENT", "expected": "SCALPEL_BLADE_10", "actual": "SCALPEL_BLADE_15" },
  "allowRetry": true,
  "nextNodeId": "1"
}
```

Puan 8 düştü. `nextNodeId` yine `1` olduğu için kursiyer aynı adımı yeniden dener.

---

## B. Unity'den

1. `docs/vr-ornek/ArqenionIlkTest.cs` dosyasını Unity projesinde `Assets/` altına kopyala.
2. Boş bir sahnede **GameObject → Create Empty** ile bir nesne oluştur ve betiği bu nesneye sürükle.
3. Inspector'da `Username` ve `Password` alanlarını doldur. `Base Url` ve `Seed` hazır gelir.
4. **Play**'e bas ve **Console** penceresine bak:

```
1) Giriş tamam: vr-gelistirici (trainee). Jeton bitişi: ...
2) Seans açıldı: 660e983c-...  puan 100
   Adım 1: Sağ alt kadranda McBurney (veya Elliot) cilt insizyonu
   Cerrah istiyor: No:10 bistüri ucu
   Görev: Cerrah istiyor: No:10 bistüri ucu. Cerraha teslim edin: No:10 bistüri ucu
3) Doğru: Doğru işlem.  puan 100
   Sıradaki adım 2: Cilt altı yağ dokusunun diseksiyonu
   Görev: Cerraha teslim edin: Monopolar elektrokoter kalemi (düz uç)
İlk test bitti.
```

Notlar:
- **`http://` adresi:** Unity `http://192.168.1.60:5080` adresine varsayılan olarak bağlanmaz. Kullanacaksan **Project Settings → Player → Other Settings → Allow downloads over HTTP** ayarını "Always allowed" yap. ngrok adresi `https` olduğu için bu ayar gerekmez.
- **Quest (Android):** aynı betik çalışır. Gözlükte internet ya da aynı Wi-Fi olmalı.

---

## Bir şey ters giderse

| Gördüğün | Sebep | Çözüm |
|---|---|---|
| JSON yerine HTML sayfası | ngrok uyarı sayfası | `ngrok-skip-browser-warning: 1` başlığını ekle |
| `401` | Jeton yok, yanlış ya da süresi dolmuş | 2. adımı tekrarla, `Bearer ` sonrasına jetonu koy |
| Girişte `401` | Kullanıcı adı ya da şifre yanlış | Eğitmenden şifre sıfırlamasını iste |
| `429` | Dakikada 10'dan fazla giriş denemesi | 1 dakika bekle |
| `400` ve `"field": "..."` | Gönderilen JSON hatalı (olay adı, alet kodu) | Cevaptaki `message` ve `allowed` listesine bak |
| `404` | `sessionId` yanlış | 3. adımdaki `sessionId`'yi kullan |
| Cevap hiç gelmiyor | Pi kapalı ya da internet yok | `$ARQ/api/health` adresini tarayıcıda aç; `ok` görmüyorsan Mennan'a haber ver |
| `"Bu olay daha önce işlendi (yinelenen)"` | Aynı `eventId` ikinci kez gönderildi | Normal; her yeni olaya yeni bir `eventId` ver |

Sonraki adım: bu döngüyü (`nextStep`'i göster → olayı gönder → cevabı işle) bütün adımlar için kur. Bkz. [vr-kilavuz.md](vr-kilavuz.md).
