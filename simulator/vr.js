// VR bağlantı testi: gözlükten gelen istekleri canlı izler ve kumanda test ekranı gibi adım adım yönlendirir.
// Kayıt backend'de (Services/VrMonitor.cs): sözleşme uçlarına gelen her istek cihaz başına tutulur, SignalR "VrActivity" ile gelir.
// Bu sayfanın kendi istekleri X-Arqenion-Monitor başlığı / ?monitor=1 ile kayda girmez.
const $ = (id) => document.getElementById(id);
const TOKEN_KEY = "arqenion.token";
const PREFS_KEY = "arqenion.vrtest";
const API = arqApiBase();   // config.js
const MONITOR = { "X-Arqenion-Monitor": "1" };
const LIVE_MS = 60_000;     // son istek bu kadar yeniyse cihaz "bağlı" sayılır

let token = null;
try { token = localStorage.getItem(TOKEN_KEY); } catch { /* gizli pencere */ }
let me = null;
let conn = null;
const clients = new Map();          // key → { client, entries }
let selected = "auto";
let followedKey = null;             // otomatik modda izlenen cihaz
let prefs = loadPrefs();            // { [baseKey|user]: { resetAt, skipped: [] } }
let lastDone = new Set();           // yeni tamamlanan adımı canlandırmak için
let lastDoneKey = null;              // lastDone hangi cihazın
let swaggerOps = null;              // "POST /api/sessions" → "#/Session/post_api_sessions"
let openStep = null;                // kullanıcının açtığı adım (yoksa sıradaki açık)

const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const time = (s) => new Date(s).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
function ago(s) {
  const sec = Math.max(0, Math.round((Date.now() - new Date(s)) / 1000));
  if (sec < 5) return "şimdi";
  if (sec < 60) return `${sec} sn önce`;
  if (sec < 3600) return `${Math.floor(sec / 60)} dk önce`;
  return `${Math.floor(sec / 3600)} sa önce`;
}
const pretty = (v) => { try { return JSON.stringify(typeof v === "string" ? JSON.parse(v) : v, null, 2); } catch { return String(v); } };

// ---------- Adımlar ----------
// match: bu kayıt adımı tamamlar mı; fail: tamamlanmadıysa son hatalı denemenin açıklaması.
const EX_SESSION = `{ "scenarioId": "case-basic-01", "seed": "ilk-test" }`;
const EX_EVENT = `{
  "eventId": "yeni-bir-uuid",
  "eventType": "instrument_delivered",
  "instrumentCode": "SCALPEL_BLADE_10",
  "target": "SURGEON"
}`;
const STEPS = [
  { id: "health", op: "health", optional: true, title: "Sunucuya ulaş", method: "GET", path: "/api/health",
    todo: `<p>Uygulama açılınca sunucuya bir sağlık isteği atsın. Giriş gerekmez.</p><p>Cevap: <code>{"status":"ok"}</code>. Gelmiyorsa adres ya da internet bağlantısı yanlıştır.</p>`,
    match: (e) => e.status === 200,
    got: (e) => `Sunucu cevap verdi · ${e.durationMs} ms` },
  { id: "login", op: "login", title: "Giriş yap", method: "POST", path: "/api/auth/login",
    todo: `<p>Eğitmenin açtığı kullanıcı adı ve şifreyle giriş yap. Cevaptaki <code>token</code> saklanır; sonraki her istekte <code>Authorization: Bearer &lt;token&gt;</code> başlığı olur.</p>`,
    example: `{ "username": "vr-gelistirici", "password": "…" }`,
    match: (e) => e.status === 200,
    got: (e) => `<b>${esc(e.detail?.username)}</b> giriş yaptı (${e.detail?.role === "instructor" ? "eğitmen" : "kursiyer"}). Jeton 8 saat geçerli.` },
  { id: "procedure", op: "procedure", title: "Alet kataloğunu al", method: "GET", path: "/api/procedure",
    todo: `<p>Tepsiyi kurmak için alet ve iplik listesini al: <code>instrumentCatalog</code>, <code>sutureCatalog</code>. Olaylarda her aletin <code>code</code> değeri gönderilir.</p>`,
    match: (e) => e.status === 200,
    got: () => `Katalog alındı. Tepsi bu listeyle kurulabilir.` },
  { id: "manifest", op: "manifest", optional: true, title: "3D model listesini al", method: "GET", path: "/api/assets/manifest",
    todo: `<p>Model listesini al. <code>sha256</code> değeri değişen dosyaları <code>url</code> adresinden indir. Şimdilik yalnız 5 örnek kutu var; <code>missing</code> listesindekiler için yer tutucu göster.</p>`,
    match: (e) => e.status === 200,
    got: (e, list) => { const n = list.filter((x) => x.op === "asset" && x.status === 200).length;
      return `Model listesi alındı${n ? ` · ${n} model dosyası indirildi` : ""}.`; } },
  { id: "session", op: "session", title: "Seansı başlat", method: "POST", path: "/api/sessions",
    todo: `<p>Seans aç. Cevaptan <code>sessionId</code> ve <code>currentStep</code> saklanır. Testte <code>seed: "ilk-test"</code> gönderilirse cerrah 1. adımda hep <b>No:10 bistüri</b> ister.</p>`,
    example: EX_SESSION,
    match: (e) => e.status === 200,
    got: (e) => `Seans <code>${esc((e.sessionId || "").slice(0, 8))}</code> açıldı · ${esc(e.detail?.scenarioName)}<br>` +
      `İlk adım: <b>${esc(e.detail?.stepTitle)}</b>${e.detail?.surgeonRequest ? ` · Cerrah istiyor: <b>${esc(e.detail.surgeonRequest)}</b>` : ""}` },
  { id: "hub", op: "hub-join", optional: true, title: "Canlı bağlantı (SignalR)", method: "HUB", path: "/hubs/simulation",
    todo: `<p><code>/hubs/simulation?access_token=&lt;token&gt;</code> adresine bağlan, sonra <code>JoinSession(sessionId)</code> çağır. Puan değişimi (<code>ScoreUpdate</code>) ve yapay zekâ açıklaması (<code>AiFeedback</code>) buradan gelir.</p>`,
    match: (e) => e.status === 200,
    got: () => `JoinSession tamam. Canlı puan ve yapay zekâ açıklaması bu cihaza gelecek.` },
  { id: "event", op: "event", title: "İlk olayı gönder", method: "POST", path: "/api/sessions/{sessionId}/events",
    todo: `<p>Kursiyer aleti cerraha uzatınca olay gönder. Her olaya yeni bir <code>eventId</code> (UUID) ver. Cevap hemen gelir.</p>`,
    example: EX_EVENT,
    match: (e) => e.status === 200,
    got: (e) => `<code>${esc(e.detail?.eventType)}</code> ${esc(e.detail?.instrumentCode || "")} → ${e.detail?.success ? "<b>doğru</b>" : "yanlış"} · ${e.durationMs} ms` },
  { id: "correct", op: "event", title: "Doğru alet → sıradaki adım", method: "POST", path: "/api/sessions/{sessionId}/events",
    todo: `<p>Cerrahın istediği aleti gönder. Cevapta <code>success: true</code> ve <code>nextStep</code> gelir; gözlük <code>nextStep</code>'i ekrana getirir.</p>`,
    example: EX_EVENT,
    match: (e) => e.status === 200 && e.detail?.success === true && !e.detail?.duplicate,
    got: (e) => `${esc(e.detail?.instrumentCode)} doğru · Sıradaki adım: <b>${esc(e.detail?.nextTitle)}</b>` },
  { id: "wrong", op: "event", optional: true, title: "Yanlış alet → hata kartı", method: "POST", path: "/api/sessions/{sessionId}/events",
    todo: `<p>Bilerek yanlış bir alet gönder (ör. <code>SCALPEL_BLADE_15</code>). Cevapta <code>success: false</code>, <code>message</code> ve <code>complication</code> gelir, puan düşer. Gözlük mesajı ve klinik açıklamayı gösterir; <code>nextStep</code> aynı adımdır.</p>`,
    example: EX_EVENT.replace("SCALPEL_BLADE_10", "SCALPEL_BLADE_15"),
    match: (e) => e.status === 200 && e.detail?.success === false,
    got: (e) => `${esc(e.detail?.actual)} verildi, ${esc(e.detail?.expected)} bekleniyordu · puan <b>${esc(e.detail?.score)}</b>` +
      (e.detail?.risk ? `<br>Olası risk: ${esc(e.detail.risk)}` : "") },
  { id: "duplicate", op: "event", optional: true, title: "Aynı olayı tekrar gönder", method: "POST", path: "/api/sessions/{sessionId}/events",
    todo: `<p>Ağ koptu gibi düşün: son olayı <b>aynı <code>eventId</code></b> ile yeniden gönder. Backend onu ikinci kez saymaz, "yinelenen" mesajıyla mevcut durumu döner.</p>`,
    match: (e) => e.status === 200 && e.detail?.duplicate === true,
    got: () => `Aynı eventId ikinci kez sayılmadı. Tekrar gönderim güvenli.` },
  { id: "contract", op: "event", optional: true, title: "Sözleşme hatası (400) nasıl görünür", method: "POST", path: "/api/sessions/{sessionId}/events",
    todo: `<p>Bilinmeyen bir olay adı gönder (ör. <code>"eventType": "instrument_give"</code>). Cevap <b>400</b> olur: bu kursiyerin değil, VR kodunun hatasıdır; puana işlemez. <code>message</code> ve <code>field</code> neyin yanlış olduğunu söyler.</p>`,
    example: `{ "eventId": "yeni-bir-uuid", "eventType": "instrument_give", "instrumentCode": "SCALPEL_BLADE_10" }`,
    match: (e) => e.status === 400,
    got: (e) => `400 · ${esc(e.detail?.message)}${e.detail?.field ? ` (alan: <code>${esc(e.detail.field)}</code>)` : ""}` },
  { id: "complete", op: "complete", title: "Bitir ve rapor al", method: "POST", path: "/api/sessions/{sessionId}/complete",
    todo: `<p>Ameliyat bitince (<code>completed: true</code>) ya da test için şimdi seansı bitir. Rapor gelir: puan, doğru/yanlış sayısı.</p>`,
    match: (e) => e.status === 200,
    got: (e) => `Rapor alındı · puan <b>${esc(e.detail?.score)}</b>, başarı %${esc(e.detail?.successRate)}` },
];

function failHint(step, e) {
  const d = e.detail || {};
  if (step.op === "login" && e.status === 401) return "Kullanıcı adı ya da şifre hatalı. Eğitmenden hesabı ya da şifreyi kontrol etmesini iste.";
  if (e.status === 429) return "Dakikada 10'dan fazla giriş denemesi. 1 dakika bekleyip tekrar dene.";
  if (e.status === 401) return "Jeton yok ya da süresi dolmuş. Authorization: Bearer <token> başlığını ekle; gerekirse yeniden giriş yap.";
  if (e.status === 404) return `${d.message || "Bulunamadı."} sessionId ya da scenarioId değerini kontrol et.`;
  if (e.op === "hub-join") return `JoinSession reddedildi: ${d.message || "seans bu kullanıcıya ait değil."}`;
  if (e.status === 400) return `Sözleşmeye uymadı: ${d.message || ""}${d.field ? ` (alan: ${d.field})` : ""}`;
  return `${e.status}: ${d.message || "beklenmeyen cevap"}`;
}

// ---------- Durum hesabı ----------
const prefKey = (c) => `${c.baseKey}|${c.userId || "anon"}`;
function loadPrefs() { try { return JSON.parse(localStorage.getItem(PREFS_KEY)) || {}; } catch { return {}; } }
function savePrefs() { try { localStorage.setItem(PREFS_KEY, JSON.stringify(prefs)); } catch { /* yalnız bu açılışta */ } }

function progressOf(item) {
  const p = prefs[prefKey(item.client)] || {};
  const since = p.resetAt ? new Date(p.resetAt) : new Date(0);
  const list = item.entries.filter((e) => new Date(e.at) >= since);
  const skipped = new Set(p.skipped || []);
  const states = STEPS.map((s) => {
    const mine = list.filter((e) => e.op === s.op);
    const hit = mine.find((e) => s.match(e));
    const lastBad = [...mine].reverse().find((e) => e.status >= 400);
    return { step: s, hit, bad: !hit && lastBad ? lastBad : null, skipped: !hit && skipped.has(s.id) };
  });
  // Önce eksik zorunlu adım; hepsi geçtiyse sıradaki isteğe bağlı adım.
  const current = states.find((x) => !x.hit && !x.skipped && !x.step.optional)
    || states.find((x) => !x.hit && !x.skipped) || null;
  const req = states.filter((x) => !x.step.optional);
  const doneReq = req.filter((x) => x.hit).length;
  const doneAll = states.filter((x) => x.hit).length;
  return { states, current, list, doneReq, totalReq: req.length, doneAll, total: states.length };
}

function sortedClients() {
  const showWeb = $("showWeb").checked;
  return [...clients.values()]
    .filter((x) => showWeb || x.client.kind !== "web" || x.client.userId)
    .sort((a, b) => new Date(b.client.lastSeen) - new Date(a.client.lastSeen));
}

// Otomatik: en son etkin VR/araç istemcisi; yoksa en son etkin herhangi biri.
function activeItem() {
  if (selected !== "auto") return clients.get(selected) || null;
  const all = [...clients.values()].sort((a, b) => new Date(b.client.lastSeen) - new Date(a.client.lastSeen));
  const pick = all.find((x) => x.client.kind !== "web") || all[0] || null;
  followedKey = pick?.client.key || null;
  return pick;
}

// ---------- Çizim ----------
function render() {
  renderPicker();
  renderWizard();
  renderGrid();
}

function kindIcon(kind) { return kind === "vr" ? "🥽" : kind === "web" ? "🌐" : "🛠️"; }
function kindName(kind) { return kind === "vr" ? "VR cihazı" : kind === "web" ? "Web tarayıcısı" : "Araç"; }

function renderPicker() {
  const sel = $("devicePick");
  const items = [...clients.values()].sort((a, b) => new Date(b.client.lastSeen) - new Date(a.client.lastSeen));
  const opts = [`<option value="auto">Otomatik: son etkin cihaz</option>`].concat(items.map((x) =>
    `<option value="${esc(x.client.key)}">${kindIcon(x.client.kind)} ${esc(x.client.label)}${x.client.username ? " · " + esc(x.client.username) : ""}</option>`));
  const html = opts.join("");
  if (sel.dataset.html !== html) { sel.innerHTML = html; sel.dataset.html = html; }
  sel.value = clients.has(selected) ? selected : "auto";
}

function renderWizard() {
  const item = activeItem();
  const dev = $("device");
  if (!item) {
    dev.className = "vt-device waiting";
    $("stateText").textContent = "Cihaz bekleniyor";
    $("stateSub").textContent = "Gözlükte uygulamayı aç; ilk istek gelince burada görünür.";
    $("deviceInfo").innerHTML = `<dt>Sunucu</dt><dd class="mono">${esc(API)}</dd>`;
    renderSteps(null);
    renderLog(null);
    setProgress(null);
    return;
  }
  const c = item.client;
  const live = Date.now() - new Date(c.lastSeen) < LIVE_MS;
  const pr = progressOf(item);
  const flash = dev.classList.contains("flash") ? " flash" : dev.classList.contains("flash-bad") ? " flash-bad" : "";
  dev.className = `vt-device ${live ? "live" : "idle"}${flash}`;
  const finished = pr.doneReq === pr.totalReq;
  $("stateText").textContent = finished ? "Test tamam 🎉" : live ? "Bağlandı" : "Bağlantı sessiz";
  $("stateSub").textContent = finished
    ? "Zorunlu adımların hepsi geçti. İsteğe bağlı adımları da deneyebilirsin."
    : pr.current ? `Sıradaki: ${pr.current.step.title}` : "";
  $("deviceInfo").innerHTML =
    `<dt>Cihaz</dt><dd>${kindIcon(c.kind)} ${esc(c.label)} <span class="muted">(${kindName(c.kind)})</span></dd>` +
    `<dt>Kullanıcı</dt><dd>${esc(c.username || "giriş yapılmadı")}</dd>` +
    (c.sessionId ? `<dt>Seans</dt><dd class="mono">${esc(c.sessionId.slice(0, 8))}${c.score != null ? ` · puan ${c.score}` : ""}${c.completed ? " · bitti" : ""}</dd>` : "") +
    `<dt>Son istek</dt><dd data-ago="${esc(c.lastSeen)}">${ago(c.lastSeen)}</dd>` +
    `<dt>İstek</dt><dd>${c.requestCount}</dd>` +
    (me?.role === "instructor" && c.ip ? `<dt>IP</dt><dd class="mono">${esc(c.ip)}</dd>` : "");
  setProgress(pr);
  renderSteps(item, pr);
  renderLog(item);
}

function setProgress(pr) {
  $("progressText").textContent = pr ? `${pr.doneReq} / ${pr.totalReq} zorunlu adım · toplam ${pr.doneAll} / ${pr.total}` : "Henüz cihaz yok";
  $("progressPct").textContent = pr ? `%${Math.round((100 * pr.doneReq) / pr.totalReq)}` : "";
  $("progressBar").style.width = pr ? `${(100 * pr.doneReq) / pr.totalReq}%` : "0";
}

function swaggerHref(method, path) {
  const base = `${API}/swagger/index.html`;
  const hash = swaggerOps?.[`${method} ${path}`];
  return hash ? base + hash : base;
}

function renderSteps(item, pr) {
  const states = pr?.states || STEPS.map((s) => ({ step: s }));
  const currentId = pr?.current?.step.id || (item ? null : "health");
  const nowDone = new Set(states.filter((x) => x.hit).map((x) => x.step.id));
  const html = states.map((x, i) => {
    const s = x.step;
    const isCur = s.id === currentId;
    const open = openStep ? openStep === s.id : isCur;
    const cls = ["vt-step", x.hit ? "done" : "", isCur ? "current" : "", x.bad ? "failed" : "", x.skipped ? "skipped" : "",
      x.hit && !lastDone.has(s.id) && lastDoneKey === item?.client.key ? "pop" : ""].filter(Boolean).join(" ");
    const chip = x.hit ? `<span class="chip ok">✓ Tamam</span>`
      : x.bad ? `<span class="chip bad">✗ Hata</span>`
      : x.skipped ? `<span class="chip">Atlandı</span>`
      : isCur ? `<span class="chip now">● Bekleniyor</span>`
      : s.optional ? `<span class="chip opt">İsteğe bağlı</span>` : `<span class="chip">Sırada</span>`;
    const small = x.hit ? stripTags(s.got(x.hit, pr.list)) : `${s.method === "HUB" ? "SignalR" : s.method} ${s.path}`;
    let body = "";
    if (open) {
      body = `<div class="vt-body">${s.todo}
        <div class="vt-endpoint"><span class="verb ${s.method}">${s.method}</span><code>${esc(s.path)}</code>
          ${s.method !== "HUB" ? `<a class="btn btn-ghost btn-mini" target="_blank" rel="noopener" href="${esc(swaggerHref(s.method, s.path))}">Swagger'da dene ↗</a>` : ""}</div>
        ${s.example ? `<div class="vt-json-wrap"><pre class="vt-json">${esc(s.example)}</pre><button class="btn btn-mini" type="button" data-copy="${esc(s.example)}">Kopyala</button></div>` : ""}
        ${x.bad ? `<div class="vt-hint">✗ ${esc(failHint(s, x.bad))} <span class="muted">(${time(x.bad.at)})</span></div>` : ""}
        ${x.hit ? `<div class="vt-got"><b>✓ Yakalandı</b> · ${time(x.hit.at)} · ${s.got(x.hit, pr.list)}</div>
          <details class="vt-raw"><summary>Kaydı gör</summary><pre class="vt-json">${esc(pretty(rawOf(x.hit)))}</pre></details>` : ""}
        ${!x.hit && !x.skipped && s.optional && item ? `<div><button class="btn btn-mini btn-ghost" type="button" data-skip="${s.id}">Bu adımı atla</button></div>` : ""}
      </div>`;
    }
    return `<article class="${cls}" data-step="${s.id}"><header><span class="vt-num">${x.hit ? "✓" : i + 1}</span>
      <div class="t">${esc(s.title)}<small>${esc(small)}</small></div>${chip}</header>${body}</article>`;
  }).join("");
  $("steps").innerHTML = html;
  // Yeni tamamlanan adım: kısa canlandırma ve bildirim
  if (item) {
    const fresh = STEPS.filter((s) => nowDone.has(s.id) && !lastDone.has(s.id));
    if (fresh.length && lastDoneKey === item.client.key) toast(`✓ Adım tamam: ${fresh[fresh.length - 1].title}`, "ok");
    lastDone = nowDone;
    lastDoneKey = item.client.key;
  }
}

const stripTags = (h) => String(h).replace(/<br>/g, " · ").replace(/<[^>]+>/g, "").replace(/&amp;/g, "&").replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&#39;/g, "'");

function rawOf(e) {
  const d = { ...(e.detail || {}) };
  if (d.request) { try { d.request = JSON.parse(d.request); } catch { /* kesik gövde */ } }
  return { at: e.at, method: e.method, path: e.path, status: e.status, durationMs: e.durationMs, sessionId: e.sessionId, ...d };
}

function summary(e) {
  const d = e.detail || {};
  if (e.status >= 400) return `<span class="st bad">✗</span> ${esc(d.message || "hata")}${d.field ? ` (alan: ${esc(d.field)})` : ""}`;
  switch (e.op) {
    case "health": return "Sunucu ayakta";
    case "login": return `${esc(d.username)} giriş yaptı`;
    case "procedure": return "Katalog okundu";
    case "manifest": return "Model listesi okundu";
    case "asset": return `Model indirildi: ${esc(d.file)}`;
    case "session": return `Seans açıldı · ilk adım: ${esc(d.stepTitle)}${d.surgeonRequest ? ` · cerrah: ${esc(d.surgeonRequest)}` : ""}`;
    case "resume": return "Seans durumu okundu";
    case "hub-connect": return "SignalR bağlandı";
    case "hub-join": return "JoinSession tamam";
    case "complete": return `Seans bitti · puan ${esc(d.score)}`;
    case "event":
      if (d.duplicate) return "Yinelenen olay (sayılmadı)";
      return `${esc(d.eventType)} ${esc(d.instrumentCode || "")} → ${d.success ? '<span class="st ok">doğru</span>' : `<span class="st bad">yanlış</span> (${esc(d.expected)} bekleniyordu)`} · puan ${esc(d.score)}` +
        (d.success && d.nextTitle ? ` · sıradaki: ${esc(d.nextTitle)}` : "");
    default: return "";
  }
}

// Seans kimliği (UUID) ilk 8 karakterle: tabloda durum ve özet sütunları görünsün.
const shortPath = (p) => String(p).replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, (m) => m.slice(0, 8) + "…");

let lastLogTop = null;   // en son gösterilen kayıt; yalnız ondan yeniler parlar
let lastLogKey = null;
function renderLog(item) {
  const rows = item ? [...item.entries].reverse().slice(0, 120) : [];
  if (item?.client.key !== lastLogKey) { lastLogTop = null; lastLogKey = item?.client.key ?? null; }
  $("logCount").textContent = item ? `${item.entries.length} kayıt` : "";
  $("logRows").innerHTML = rows.length ? rows.map((e) => {
    const st = e.status >= 400 ? "bad" : e.status >= 300 ? "warn" : "ok";
    const isNew = lastLogTop !== null && e.id > lastLogTop;
    return `<li class="${isNew ? "new" : ""}"><div class="req"><span class="mono muted">${time(e.at)}</span>
      <span class="verb ${e.method}">${esc(e.method)}</span><code title="${esc(e.path)}">${esc(shortPath(e.path))}</code>
      <span class="st ${st}">${e.status}</span>${e.method === "HUB" ? "" : `<span class="mono muted">${e.durationMs} ms</span>`}</div>
      <div class="sum">${summary(e)}</div></li>`;
  }).join("") : `<li class="muted">Henüz istek yok. Gözlükten ilk istek gelince burada görünür.</li>`;
  if (rows.length) lastLogTop = rows[0].id;
}

function renderGrid() {
  const items = sortedClients();
  $("allCount").textContent = clients.size ? ` (${items.length})` : "";
  $("allNote").textContent = me?.role === "instructor"
    ? "Bütün cihazlar burada. Bir karta tıklayınca o cihazın test adımları açılır."
    : "Yalnız senin hesabınla gelen cihazlar görünür. Eğitmen hesabı bütün cihazları görür.";
  $("grid").innerHTML = items.length ? items.map((x) => {
    const c = x.client;
    const pr = progressOf(x);
    const live = Date.now() - new Date(c.lastSeen) < LIVE_MS;
    const last = x.entries[x.entries.length - 1];
    const finished = pr.doneReq === pr.totalReq;
    return `<button class="vt-card" type="button" data-key="${esc(c.key)}">
      <div class="top"><span class="ico">${kindIcon(c.kind)}</span>
        <div style="min-width:0;flex:1"><div class="name">${esc(c.label)}</div><div class="meta">${esc(c.username || "giriş yapılmadı")} · ${kindName(c.kind)}</div></div>
        <span class="dot ${live ? "live" : "idle"}" title="${live ? "Bağlı" : "Sessiz"}"></span></div>
      <div class="vt-bar"><i style="width:${(100 * pr.doneReq) / pr.totalReq}%"></i></div>
      <div class="meta">${finished ? "✓ Zorunlu adımlar tamam" : `${pr.doneReq}/${pr.totalReq} adım · sıradaki: ${esc(pr.current?.step.title || "—")}`}</div>
      <div class="meta">${last ? `${esc(last.method)} ${esc(shortPath(last.path))} · <span class="st ${last.status >= 400 ? "bad" : "ok"}">${last.status}</span>` : ""}</div>
      <div class="meta"><span data-ago="${esc(c.lastSeen)}">${ago(c.lastSeen)}</span>${c.sessionId ? ` · seans ${esc(c.sessionId.slice(0, 8))}` : ""}${c.score != null ? ` · puan ${c.score}` : ""}</div>
    </button>`;
  }).join("") : `<div class="vt-empty" style="grid-column:1/-1"><div style="font-size:34px">🥽</div><div>Henüz cihaz yok.</div>
      <div>Gözlük ya da test aracı <code>${esc(API)}</code> adresine istek attığında burada belirir.</div></div>`;
}

let toastTimer = null;
function toast(text, kind) {
  const t = $("toast");
  t.textContent = text;
  t.className = `vt-toast show ${kind || ""}`;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove("show"), 3500);
}

function flashDevice(bad) {
  const dev = $("device");
  dev.classList.remove("flash", "flash-bad");
  void dev.offsetWidth;
  dev.classList.add(bad ? "flash-bad" : "flash");
  setTimeout(() => dev.classList.remove("flash", "flash-bad"), 900);
}

// ---------- Canlı veri ----------
function upsert(snapshot) {
  if (snapshot.removedKey) clients.delete(snapshot.removedKey);
  clients.set(snapshot.client.key, { client: snapshot.client, entries: snapshot.entries || [] });
  if (selected === snapshot.removedKey) selected = snapshot.client.key;
}

function onActivity({ client, entry }) {
  const item = clients.get(client.key) || { client, entries: [] };
  item.client = client;
  if (!item.entries.some((e) => e.id === entry.id)) item.entries.push(entry);
  if (item.entries.length > 200) item.entries.splice(0, item.entries.length - 200);
  clients.set(client.key, item);
  const shown = activeItem();
  if (shown?.client.key === client.key) {
    flashDevice(entry.status >= 400);
    if (entry.status >= 400) toast(`✗ ${entry.method} ${shortPath(entry.path)} · ${entry.status}`, "bad");
    else toast(`İstek yakalandı: ${entry.method} ${shortPath(entry.path)} · ${entry.status}${entry.method === "HUB" ? "" : ` · ${entry.durationMs} ms`}`);
  }
  render();
  const card = document.querySelector(`.vt-card[data-key="${CSS.escape(client.key)}"]`);
  if (card) { card.classList.add("flash"); setTimeout(() => card.classList.remove("flash"), 900); }
}

async function connectHub() {
  if (!window.signalR) { setLive(false, "SignalR yüklenemedi"); return; }
  conn = new signalR.HubConnectionBuilder()
    .withUrl(`${API}/hubs/simulation?monitor=1`, { accessTokenFactory: () => token, headers: arqExtraHeaders(API) })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
    .build();
  conn.on("VrActivity", onActivity);
  conn.on("VrClient", (s) => { upsert(s); render(); });
  conn.onreconnecting(() => setLive(false, "Yeniden bağlanıyor…"));
  conn.onreconnected(() => watch());
  conn.onclose(() => setLive(false));
  try { await conn.start(); await watch(); }
  catch (e) { setLive(false, "Canlı bağlantı kurulamadı"); console.warn(e); setTimeout(connectHub, 5000); }
}

async function watch() {
  const snap = await conn.invoke("WatchVr");
  clients.clear();
  for (const s of snap) upsert(s);
  setLive(true);
  render();
}

function setLive(on, text) {
  const b = $("liveBadge");
  b.className = `conn-badge ${on ? "conn-on" : "conn-off"}`;
  b.textContent = on ? "● Canlı izleniyor" : `● ${text || "Canlı bağlantı yok"}`;
}

// Swagger derin bağlantıları: swagger.json'daki etiket + işlem adından (Swagger UI'nin kendi kimlik kuralı).
async function loadSwagger() {
  $("swaggerLink").href = `${API}/swagger/index.html`;
  try {
    const doc = await (await fetch(`${API}/swagger/v1/swagger.json`, { headers: { ...arqExtraHeaders(API), ...MONITOR } })).json();
    const map = {};
    for (const [path, ops] of Object.entries(doc.paths || {}))
      for (const [method, op] of Object.entries(ops)) {
        const tag = (op.tags || ["default"])[0];
        const id = op.operationId || `${method}${path}`.replace(/[^a-zA-Z0-9]/g, "_");
        map[`${method.toUpperCase()} ${path}`] = `#/${encodeURIComponent(tag)}/${id}`;
      }
    swaggerOps = map;
    render();
  } catch { /* Swagger kapalıysa ana sayfaya link */ }
}

// ---------- Giriş ----------
async function api(method, path, body) {
  const res = await fetch(API + path, {
    method, headers: { "Content-Type": "application/json", ...arqExtraHeaders(API), ...MONITOR, ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (res.status === 401) { setToken(null); showLogin("Oturum süreniz doldu; yeniden giriş yapın."); throw new Error("401"); }
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.message || `HTTP ${res.status}`);
  return data;
}

function setToken(t) {
  token = t;
  try { t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY); } catch {}
}

function showLogin(message) {
  $("app").classList.add("hidden");
  $("logoutBtn").classList.add("hidden");
  $("loginForm").classList.remove("hidden");
  $("loginError").textContent = message || "";
  $("loginError").classList.toggle("hidden", !message);
  $("loginUser").focus();
}

async function enter() {
  try { me = await api("GET", "/api/auth/me"); } catch { return; }
  $("who").textContent = `${me.name || me.username} · ${me.role === "instructor" ? "eğitmen" : "kursiyer"}`;
  $("adminLink").classList.toggle("hidden", me.role !== "instructor");
  $("loginForm").classList.add("hidden");
  $("app").classList.remove("hidden");
  $("logoutBtn").classList.remove("hidden");
  render();
  await connectHub();
}

$("loginForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const res = await fetch(`${API}/api/auth/login`, {
    method: "POST", headers: { "Content-Type": "application/json", ...arqExtraHeaders(API), ...MONITOR },
    body: JSON.stringify({ username: $("loginUser").value.trim(), password: $("loginPass").value }),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) { showLogin(res.status === 429 ? "Çok fazla deneme. Bir dakika sonra tekrar deneyin." : data.message); return; }
  setToken(data.token);
  $("loginPass").value = "";
  await enter();
});
$("logoutBtn").addEventListener("click", async () => { setToken(null); try { await conn?.stop(); } catch {} showLogin(); });

// ---------- Etkileşim ----------
function setView(v) {
  const wiz = v === "wizard";
  $("tabWizard").setAttribute("aria-selected", wiz);
  $("tabAll").setAttribute("aria-selected", !wiz);
  $("viewWizard").classList.toggle("hidden", !wiz);
  $("viewAll").classList.toggle("hidden", wiz);
}
$("tabWizard").addEventListener("click", () => setView("wizard"));
$("tabAll").addEventListener("click", () => setView("all"));
$("showWeb").addEventListener("change", renderGrid);
$("devicePick").addEventListener("change", (e) => { selected = e.target.value; openStep = null; render(); });
$("grid").addEventListener("click", (e) => {
  const card = e.target.closest(".vt-card");
  if (!card) return;
  selected = card.dataset.key; openStep = null;
  setView("wizard"); render(); window.scrollTo({ top: 0, behavior: "smooth" });
});
$("steps").addEventListener("click", (e) => {
  const skip = e.target.closest("[data-skip]");
  const item = activeItem();
  if (skip && item) {
    const k = prefKey(item.client);
    prefs[k] = prefs[k] || {};
    prefs[k].skipped = [...new Set([...(prefs[k].skipped || []), skip.dataset.skip])];
    savePrefs(); openStep = null; render(); return;
  }
  if (e.target.closest("[data-copy], a, details, pre")) return;
  const head = e.target.closest(".vt-step > header");
  if (!head) return;
  const id = head.parentElement.dataset.step;
  const it = activeItem();
  const cur = it ? progressOf(it).current?.step.id : "health";
  const isOpen = openStep ? openStep === id : id === cur;
  openStep = isOpen ? "__none" : id;
  render();
});
$("resetBtn").addEventListener("click", () => {
  const item = activeItem();
  if (!item) return;
  prefs[prefKey(item.client)] = { resetAt: new Date().toISOString(), skipped: [] };
  savePrefs(); openStep = null; render();
  toast("Test baştan başladı. Gözlükte uygulamayı yeniden çalıştır.");
});
document.addEventListener("click", async (e) => {
  const btn = e.target.closest("[data-copy], [data-copy-from]");
  if (!btn) return;
  const text = btn.dataset.copy ?? $(btn.dataset.copyFrom)?.textContent ?? "";
  try { await navigator.clipboard.writeText(text); btn.textContent = "Kopyalandı"; setTimeout(() => (btn.textContent = "Kopyala"), 1500); } catch { /* izin yok */ }
});

// "x sn önce" yazıları ve bağlı/sessiz durumu saniyede bir tazelenir.
setInterval(() => {
  document.querySelectorAll("[data-ago]").forEach((el) => (el.textContent = ago(el.dataset.ago)));
  const item = activeItem();
  if (item) {
    const live = Date.now() - new Date(item.client.lastSeen) < LIVE_MS;
    const dev = $("device");
    if (dev.classList.contains("live") !== live) renderWizard();
  }
}, 1000);

$("apiBase").textContent = API;
$("ngrokNote").classList.toggle("hidden", !/ngrok/.test(API));
loadSwagger();
token ? enter() : showLogin();
