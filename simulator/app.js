// Arqenion — Ameliyathane simülasyonu (web).
// Unity/VR olmadan tüm eğitim döngüsünü görsel olarak test etmek için event üretir.

// ---- Katalog ve adımlar backend'den (/api/procedure) yüklenir; burada yalnızca ikonlar ve
//      backend erişilemezse kullanılacak asgari yedek tanım tutulur. ----
const ICONS = {
  SCALPEL_NO_15: "🔪", SCALPEL_NO_15_USED: "🩸", SCALPEL_NO_11: "🔪", SCALPEL_NO_20: "🔪",
  SCALPEL_HANDLE_NO_3: "🖊️", SCALPEL_HANDLE_NO_4: "🖊️",
  MONOPOLAR_CAUTERY: "⚡", BIPOLAR_CAUTERY: "🔌", YANKAUER_SUCTION_TIP: "🌪️",
  ELECTROCAUTERY_UNIT: "🖥️", SUCTION_UNIT: "🖥️",
  MAYO_SCISSORS: "✂️", METZENBAUM_SCISSORS: "✂️",
  STILLE_FORCEPS: "🥢", SEMKEN_FORCEPS: "🥢", DEBAKEY_FORCEPS: "🥢",
  FARABEUF_RETRACTOR: "🪝", ROUX_RETRACTOR_14: "🪝", ROUX_RETRACTOR_15: "🪝",
  LANGENBECK_RETRACTOR: "🪝", KOCHER_RETRACTOR_40: "🪝", KOCHER_RETRACTOR_80: "🪝",
  BABCOCK_ATRAUMATIC: "🗜️", ATRAUMATIC_CLAMP: "🗜️", ALLIS_CLAMP_TOOTHED: "⚙️", ALLIS_CLAMP_ATRAUMATIC: "🗜️",
  MOSQUITO_CLAMP: "🗜️", PEAN_CLAMP: "🗜️", CRILE_CLAMP_STRAIGHT: "📎", CRILE_CLAMP_CURVED: "🗜️",
  CRILE_RANKIN_CLAMP: "🗜️", NISSEN_CLAMP: "🗜️", HEISS_CLAMP: "🗜️", OCHSNER_KOCHER_CLAMP: "⚙️",
  SCHMIDT_TONSIL_FORCEPS: "🗜️", OVERHOLT_CLAMP: "🗜️", GEMINI_CLAMP: "🗜️",
  FOERSTER_CLAMP: "🗜️", BACKHAUS_TOWEL_CLAMP: "📌", NEEDLE_HOLDER: "🪡",
  IRRIGATION_SYRINGE_50: "💉", SYRINGE_20: "💉",
  SAVLON_SOLUTION: "🧴", POVIDONE_IODINE: "🟤",
  GAUZE_5X5: "⬜", GAUZE_10X10: "🩹", WET_COMPRESS: "🧻", ADHESIVE_TAPE: "📏",
  DRAPE_TABLE_CHANGE: "🔄", STEEL_BOWL: "🥣", CONTAINER_LID: "📦", WIRE_BASKET: "🧺",
};
const CATEGORY_ICON = { Klemp: "🗜️", Penset: "🥢", Ekartör: "🪝", Makas: "✂️", Kesici: "🔪", Portegü: "🪡", Cihaz: "🖥️", "Cihaz Ucu": "⚡", Sarf: "⬜", Solüsyon: "🧴", Örtü: "📌", Kap: "🥣", Konteyner: "📦",
  // 60 adımlık model (v2.4) kategorileri
  blade: "🔪", handle: "🖊️", energy: "⚡", forceps: "🥢", retractor: "🪝", clamp: "🗜️", scissors: "✂️",
  needle_holder: "🪡", sponge: "⬜", antiseptic: "🟤", fluid: "💧", syringe: "💉", suction: "🌪️",
  container: "🧪", dressing: "🩹" };
// v2.4 kategori kodlarının tepsideki Türkçe adı.
const CATEGORY_LABEL = { blade: "Bistüri ucu", handle: "Bistüri sapı", energy: "Koter", forceps: "Penset", retractor: "Ekartör",
  clamp: "Klemp", scissors: "Makas", needle_holder: "Portegü", sponge: "Kompres/Spanç", antiseptic: "Antiseptik",
  fluid: "Sıvı", syringe: "Enjektör", suction: "Aspiratör", container: "Kap", dressing: "Pansuman" };
const catLabel = (c) => CATEGORY_LABEL[c] || c;
const iconFor = (item) => ICONS[item.code] || CATEGORY_ICON[item.category] || "🔧";

let INSTRUMENT_CATALOG = [];
let SUTURE_CATALOG = [];
let STEPS = [];
let PHASE_COUNT = 22;
let TOTAL_STEPS = 0;
let PROCEDURE_MODEL = "v1";
let FLOW_GRAPH = [];        // 60 adımlık model: düğümler, düz devam ve karar dalları (akış şeması için)
let FLOW_PHASES = {};       // aşama kimliği → "no · ad"   // "v2.4": 60 adımlık akış modeli (dallar, onay ve sayım adımları)

// Adımın kimliği: 60 adımlık modelde düğüm ("43a"), eski modelde adım numarası.
const keyOf = (s) => (s?.nodeId ?? s?.stepId);
const isFlowModel = () => PROCEDURE_MODEL !== "v1";

// Adım başlığından kısa bir görev cümlesi türetir (backend'de ayrı bir "instruction" alanı yok).
function instructionOf(s) {
  const names = (s.allowedInstruments || []).map(labelOf).join(" / ");
  if (s.expectedEvent === "count_confirmed") return "Kompres/alet/iğne sayımını yapıp onaylayın.";
  const zone = s.target === "DIRTY_AREA" ? "KİRLİ ALANA"
    : s.target === "PATHOLOGY_CONTAINER" ? "PATOLOJİ KABINA" : "Cerraha";
  const suture = s.requiredSuture ? ` + ${sutureLabelOf(s.requiredSuture)}` : "";
  return `${zone} teslim edin: ${names}${suture}`;
}

async function loadProcedure() {
  const p = await getJson("/api/procedure");
  PROCEDURE_MODEL = p.procedureModel || "v1";
  INSTRUMENT_CATALOG = p.instrumentCatalog.map((i) => ({ ...i, icon: iconFor(i) }));
  SUTURE_CATALOG = p.sutureCatalog;
  PHASE_COUNT = p.phaseCount || 22;
  if (isFlowModel()) {
    // 60 adımlık model: adımlar backend'in StepView'ı olarak gelir (talimat dahil).
    STEPS = p.steps.map((s) => ({
      id: s.nodeId, stepNo: s.stepId, phaseNo: s.phaseNo, phase: s.phase, title: s.title,
      event: s.expectedEvent, kind: s.kind, target: s.target, allowed: s.allowedInstruments || [],
      suture: s.requiredSuture || undefined, sutures: s.requiredSutures || [], note: s.clinicalNote,
      isBranch: s.isBranch, condition: s.condition, conditionText: s.conditionText, instruction: s.instruction,
    }));
    TOTAL_STEPS = p.mainStepCount || 60;
    FLOW_GRAPH = p.graph || [];
    FLOW_PHASES = Object.fromEntries(p.steps.filter((s) => s.phaseNo).map((s) => [s.phaseNo, s.phase]));
    return p;
  }
  STEPS = p.steps.map((s) => ({
    id: s.stepId, phaseNo: s.phaseNo, phase: s.phase, title: s.title,
    event: s.expectedEvent, target: s.target, allowed: s.allowedInstruments || [],
    suture: s.requiredSuture || undefined, counts: s.expectedCounts || undefined,
    note: s.clinicalNote, sourceSteps: s.sourceSteps || [],
  }));
  STEPS.forEach((s) => { s.instruction = instructionOf({ ...s, allowedInstruments: s.allowed, expectedEvent: s.event, requiredSuture: s.suture }); });
  TOTAL_STEPS = STEPS.length;
  return p;
}

const stepMeta = (id) => STEPS.find((s) => s.id === id);
// İlerleme göstergesi için adım numarası (alt akış "47b" → 47).
const stepNoOf = (step) => step?.stepId ?? stepMeta(keyOf(step))?.stepNo ?? 0;

const ZONE_MAP = {
  SURGEON: { eventType: "instrument_delivered", target: "SURGEON" },
  DIRTY_AREA: { eventType: "instrument_placed", target: "DIRTY_AREA" },
  PATHOLOGY_CONTAINER: { eventType: "instrument_placed", target: "PATHOLOGY_CONTAINER" },
};

// ---- Durum ----
const state = {
  sessionId: null,
  currentStep: null,
  connection: null,
  holding: null,
  activeComplication: null,
  visited: new Set(),     // 60 adımlık modelde tamamlanan düğümler (atlanan dallar boş kalır)
  openBranch: null,       // girilen dal (komplikasyon / ek durum)
  counters: { correct: 0, wrong: 0, sterile: 0, hint: 0, complication: 0, resolved: 0 },
  timeline: [],
};
let trafficCount = 0;

const monitor = {
  stress: 0,
  heartRate: 76,
  pulseRate: 74,
  respRate: 14,
  targetHeartRate: 76,
  targetPulseRate: 74,
  targetRespRate: 14,
  breathPhase: 0,
  wavePhase: 0,
  lastTick: 0,
  timerId: null,
  currentRiskScore: 0,
  currentStreak: 0,
  audioEnabled: true,
  audioReady: false,
  audioCtx: null,
  noiseSource: null,
  noiseGain: null,
};

// ---- DOM kısayolları ----
const $ = (id) => document.getElementById(id);
const els = {
  baseUrl: $("baseUrl"), scenario: $("scenario"), userId: $("userId"), logoutBtn: $("logoutBtn"),
  loginModal: $("loginModal"), loginForm: $("loginForm"), loginUser: $("loginUser"), loginPass: $("loginPass"),
  loginError: $("loginError"), loginBtn: $("loginBtn"),
  startBtn: $("startBtn"), endBtn: $("endBtn"), helpBtn: $("helpBtn"),
  scoreValue: $("scoreValue"), progressBar: $("progressBar"), progressLabel: $("progressLabel"), connState: $("connState"),
  toast: $("toast"),
  taskCard: $("taskCard"), verdictBanner: $("verdictBanner"),
  cntCorrect: $("cntCorrect"), cntWrong: $("cntWrong"), cntSterile: $("cntSterile"), cntHint: $("cntHint"),
  cntComp: $("cntComp"), cntResolved: $("cntResolved"),
  compBoard: $("compBoard"),
  hrValue: $("hrValue"), pulseValue: $("pulseValue"), respValue: $("respValue"), riskValue: $("riskValue"),
  monitorState: $("monitorState"), vitalWave: $("vitalWave"), audioToggle: $("audioToggle"),
  aiCard: $("aiCard"),
  handHolding: $("handHolding"), sutureWrap: $("sutureWrap"), sutureSelect: $("sutureSelect"),
  autoBtn: $("autoBtn"), releaseBtn: $("releaseBtn"), confirmBtn: $("confirmBtn"),
  countMode: $("countMode"), countSendBtn: $("countSendBtn"), countHelp: $("countHelp"),
  checklistMode: $("checklistMode"), checklistItems: $("checklistItems"), checklistSendBtn: $("checklistSendBtn"),
  cntCompress: $("cntCompress"), cntInstrument: $("cntInstrument"), cntNeedle: $("cntNeedle"),
  trayWrap: $("trayWrap"), instrumentGrid: $("instrumentGrid"),
  trayChips: $("trayChips"), traySearch: $("traySearch"), trayExpectedBtn: $("trayExpectedBtn"),
  trayPrev: $("trayPrev"), trayNext: $("trayNext"),
  stepList: $("stepList"),
  log: $("log"), logToggle: $("logToggle"), clearLogBtn: $("clearLogBtn"),
  traffic: $("traffic"), trafficCount: $("trafficCount"), trafficClear: $("trafficClear"), trafficToggle: $("trafficToggle"),
  reportModal: $("reportModal"), reportBody: $("reportBody"), closeReportBtn: $("closeReportBtn"),
  reportUser: $("reportUser"), reportChart: $("reportChart"), reportChartLegend: $("reportChartLegend"), reportFlow: $("reportFlow"),
  helpModal: $("helpModal"), closeHelpBtn: $("closeHelpBtn"),
  flowBtn: $("flowBtn"), flowModal: $("flowModal"), closeFlowBtn: $("closeFlowBtn"), flowDiagram: $("flowDiagram"),
  flowPlan: $("flowPlan"), flowRequests: $("flowRequests"), flowSummary: $("flowSummary"),
  bugBtn: $("bugBtn"), bugModal: $("bugModal"), closeBugBtn: $("closeBugBtn"), bugSendBtn: $("bugSendBtn"),
  bugTitle: $("bugTitle"), bugBody: $("bugBody"), bugSeverity: $("bugSeverity"), bugArea: $("bugArea"),
  bugReporter: $("bugReporter"), bugResult: $("bugResult"),
};

// ---- Yardımcılar ----
const baseUrl = () => els.baseUrl.value.replace(/\/+$/, "");
const nowIso = () => new Date().toISOString();
const timeLabel = () => new Date().toLocaleTimeString("tr-TR");
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

function escapeHtml(v) {
  return String(v ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function toast(kind, msg) {
  els.toast.textContent = msg;
  els.toast.className = `toast toast-${kind}`;
  clearTimeout(toast._t);
  toast._t = setTimeout(() => els.toast.classList.add("hidden"), 2600);
}

// Alt konsol: her isteği KAYNAĞINI vurgulayarak listeler.
function traffic(kind, source, srcClass, dir, label, detail, minor = "") {
  const row = document.createElement("div");
  row.className = `tr-row tr-${kind}`;
  const cell = (cls, txt) => { const s = document.createElement("span"); s.className = cls; s.textContent = txt; return s; };
  row.append(
    cell("tr-time", timeLabel()),
    cell(`src ${srcClass}`, source),
    cell("tr-dir", dir),
    cell("tr-label", label),
    cell("tr-detail", detail || ""),
    cell("tr-minor", minor || "")
  );
  els.traffic.appendChild(row);
  els.traffic.scrollTop = els.traffic.scrollHeight;
  trafficCount++;
  els.trafficCount.textContent = `${trafficCount} istek`;
}

function briefReq(body) {
  if (!body) return "";
  if (body.eventType) {
    let s = body.eventType;
    if (body.instrumentCode) s += ` · ${body.instrumentCode}`;
    if (body.target) s += ` → ${body.target}`;
    if (body.sutureCode) s += ` · ${body.sutureCode}`;
    if (body.counts) s += ` · ${JSON.stringify(body.counts)}`;
    return s;
  }
  if (body.scenarioId) return `scenario=${body.scenarioId}`;
  return "";
}

function briefRes(data) {
  if (!data) return "";
  if (data.deviation) return `SAPMA ${data.deviation.deviationType} · skor=${data.score}`;
  if (data.completed) return `TAMAMLANDI · skor=${data.score}`;
  if (typeof data.score === "number" && data.currentStep) return `seans · adım ${keyOf(data.currentStep)} · skor=${data.score}`;
  if (typeof data.score === "number") return `OK · skor=${data.score}`;
  return "OK";
}

function log(kind, tag, content) {
  const entry = document.createElement("div");
  entry.className = `log-entry log-${kind}`;
  const head = document.createElement("div");
  head.className = "le-head";
  head.innerHTML = `<span class="le-tag">${tag}</span><span class="le-time">${timeLabel()}</span>`;
  entry.appendChild(head);
  if (content) {
    const pre = document.createElement("pre");
    pre.textContent = typeof content === "string" ? content : JSON.stringify(content, null, 2);
    entry.appendChild(pre);
  }
  els.log.appendChild(entry);
  els.log.scrollTop = els.log.scrollHeight;
}

// ---- Kimlik: giriş jetonu (JWT) her isteğe ve SignalR bağlantısına eklenir ----
const TOKEN_KEY = "arqenion.token";
const auth = { token: null, user: null };
try { auth.token = localStorage.getItem(TOKEN_KEY); } catch { /* gizli pencere: her açılışta giriş */ }

const authHeaders = () => (auth.token ? { Authorization: `Bearer ${auth.token}` } : {});

function setAuth(token, user) {
  auth.token = token; auth.user = user;
  try { token ? localStorage.setItem(TOKEN_KEY, token) : localStorage.removeItem(TOKEN_KEY); } catch {}
  els.userId.value = user ? `${user.name || user.username}${user.role === "instructor" ? " (eğitmen)" : ""}` : "";
  els.logoutBtn.classList.toggle("hidden", !user);
  document.getElementById("adminLink")?.classList.toggle("hidden", user?.role !== "instructor");
}

function showLogin(message) {
  els.loginError.textContent = message || "";
  els.loginError.classList.toggle("hidden", !message);
  els.loginModal.classList.remove("hidden");
  setTimeout(() => (els.loginUser.value ? els.loginPass : els.loginUser).focus(), 0);
}

async function login(e) {
  e.preventDefault();
  els.loginBtn.disabled = true;
  try {
    const res = await fetch(`${baseUrl()}/api/auth/login`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username: els.loginUser.value.trim(), password: els.loginPass.value }),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      showLogin(res.status === 429 ? "Çok fazla deneme. Bir dakika sonra tekrar deneyin." : (data.message || "Giriş yapılamadı."));
      return;
    }
    setAuth(data.token, data.user);
    els.loginPass.value = "";
    els.loginModal.classList.add("hidden");
    toast("good", `Hoş geldiniz, ${data.user.name || data.user.username}.`);
  } catch {
    showLogin("Sunucuya ulaşılamadı. Backend adresini kontrol edin.");
  } finally {
    els.loginBtn.disabled = false;
  }
}

async function restoreAuth() {
  if (!auth.token) { setAuth(null, null); showLogin(); return; }
  try {
    const res = await fetch(`${baseUrl()}/api/auth/me`, { headers: authHeaders() });
    if (!res.ok) throw new Error();
    setAuth(auth.token, await res.json());
  } catch {
    setAuth(null, null);
    showLogin("Oturum süreniz doldu; yeniden giriş yapın.");
  }
}

function onUnauthorized() {
  setAuth(null, null);
  showLogin("Oturum süreniz doldu; yeniden giriş yapın.");
}

// ---- Backend çağrıları ----
async function postJson(path, body) {
  traffic("req", "VR·SİM", "src-sim", "→", `POST ${path}`, briefReq(body));
  const res = await fetch(`${baseUrl()}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(body),
  });
  if (res.status === 401) onUnauthorized();
  const text = await res.text();
  let data;
  try { data = text ? JSON.parse(text) : {}; } catch { data = { raw: text }; }
  if (res.ok) traffic("res", "BACKEND", "src-backend", "←", `${res.status} ${path}`, briefRes(data));
  else traffic("err", "HATA", "src-err", "←", `${res.status} ${path}`, JSON.stringify(data));
  if (!res.ok) { const e = new Error(`HTTP ${res.status}`); e.data = data; throw e; }
  return data;
}

async function getJson(path) {
  traffic("req", "VR·SİM", "src-sim", "→", `GET ${path}`, "");
  const res = await fetch(`${baseUrl()}${path}`, { headers: authHeaders() });
  if (res.status === 401) onUnauthorized();
  const data = await res.json();
  if (!res.ok) { const e = new Error(`HTTP ${res.status}`); e.data = data; throw e; }
  traffic("res", "BACKEND", "src-backend", "←", `${res.status} ${path}`,
    Array.isArray(data) ? `${data.length} kayıt` : "");
  return data;
}

// ---- Seans yaşam döngüsü ----
async function startSession() {
  if (!auth.token) { showLogin(); return; }
  const body = { scenarioId: els.scenario.value };
  resetCounters();
  log("info", "→ Seans Başlat", body);
  try {
    const data = await postJson("/api/sessions", body);
    state.sessionId = data.sessionId;
    setScore(data.score);
    setStep(data.currentStep);
    els.startBtn.disabled = true;
    els.endBtn.disabled = false;
    setVerdict("idle", `Seans başladı — ${data.scenarioName}`);
    toast("info", "Seans başladı. İlk görevinizi yapın.");
    log("success", "← Seans Başladı", data);
    if (monitor.audioEnabled) { ensureAudio(); startHeartbeat(); }
    await connectHub();
  } catch (e) {
    log("error", "✕ Başlatma Hatası", formatError(e));
    toast("bad", "Backend'e bağlanılamadı. Çalışıyor mu? (dotnet run)");
  }
}

async function endSession() {
  if (!state.sessionId) return;
  const sid = state.sessionId;
  try {
    const report = await postJson(`/api/sessions/${sid}/complete`, {});
    log("success", "← Rapor", report);
    let events = [];
    try { events = await getJson(`/api/sessions/${sid}/events`); } catch {}
    showReport(report, events);
  } catch (e) {
    log("error", "✕ Bitirme Hatası", formatError(e));
  } finally {
    stopHeartbeat();
    await disconnectHub();
    state.sessionId = null;
    state.currentStep = null;
    release();
    els.startBtn.disabled = false;
    els.endBtn.disabled = true;
  }
}

// ---- Olay gönderme ----
async function sendEvent(fields) {
  if (!state.sessionId) { toast("bad", "Önce seansı başlatın."); return; }
  const payload = { eventId: crypto.randomUUID(), timestamp: nowIso(), ...fields };
  log("info", `→ ${fields.eventType}`, payload);
  try {
    const data = await postJson(`/api/sessions/${state.sessionId}/events`, payload);
    handleResponse(fields.eventType, data);
  } catch (e) {
    log("error", "✕ Olay Hatası", formatError(e));
    toast("bad", "Olay gönderilemedi.");
  }
}

function handleResponse(sentType, data) {
  if (typeof data.score === "number") setScore(data.score);
  const replay = (data.message || "").includes("yinelenen");

  // Senaryo komplikasyonu: açıldı / ilerledi / çözüldü.
  if (data.activeComplication) {
    const ac = data.activeComplication;
    const wasOpen = !!state.activeComplication && !state.activeComplication.resolved;
    state.activeComplication = ac.resolved ? null : ac;
    renderActiveComplication(ac);
    if (!wasOpen) {
      // Komplikasyon ilk kez bildirildi (çözülebilir veya değil).
      state.counters.complication++;
      toast("bad", `KOMPLİKASYON — ${ac.title}`);
      log("error", `🩸 Komplikasyon: ${ac.title}`, `${ac.narrative}\n\n${ac.resolutionNote}`);
      traffic("ai", "KOMPLİKASYON", "src-aitable", "🩸", ac.title, ac.narrative);
      shockMonitor(ac.severity);
    } else if (ac.resolved) {
      state.counters.resolved++;
      toast("good", `Komplikasyon giderildi — ${ac.title}`);
      log("success", "🩹 Komplikasyon giderildi", ac.resolutionNote);
      relaxMonitor();
    }
  }

  // 60 adımlık model: karar noktasında dala girildi ya da daldan ana akışa dönüldü.
  if (data.branch && !replay) handleBranch(data.branch);

  if (data.success && data.currentNodeId && !replay && data.nextNodeId !== data.currentNodeId) state.visited.add(data.currentNodeId);

  if (data.deviation) {
    const d = data.deviation;
    if (!replay) {
      state.counters.wrong++;
      if (d.deviationType === "STERILE_VIOLATION") state.counters.sterile++;
    }
    setVerdict("bad", `SAPMA: ${d.deviationType}`);
    toast("bad", `${d.deviationType} — ${data.message || ""}`);
    log("error", `⚠ Sapma: ${d.deviationType}`,
      `Beklenen: ${d.expected ?? "-"}\nGerçekleşen: ${d.actual ?? "-"}\nŞiddet: ${d.severity ?? "-"}\n${data.message ?? ""}`);
    // Komplikasyon REST yanıtıyla anında gelir (SignalR'a bağlı değil).
    if (data.complication) {
      traffic("ai", "AI·TABLO", "src-aitable", "⚑", "Komplikasyon (deterministik)",
        `${data.complication.deviationType} · ${data.complication.possibleRisk || ""}`);
      showAiFeedback(data.complication);
    }
    // Kümülatif komplikasyon panosu + hasta monitörü tepkisi.
    if (data.cumulativeComplication) {
      renderComplications(data.cumulativeComplication);
      applyDeviationToMonitor(data.cumulativeComplication, d.severity);
    }
  } else if (sentType === "hint_requested") {
    if (!replay) state.counters.hint++;
    setVerdict("info", "İPUCU");
    aiState("info", `💡 ${data.message || "İpucu verildi."}`);
    toast("info", data.message || "İpucu verildi.");
    log("info", "💡 İpucu", data.message || "");
  } else {
    if (!replay) state.counters.correct++;
    const next = data.nextStep ? ` — sıradaki: Adım ${keyOf(data.nextStep)} · ${data.nextStep.title}` : "";
    setVerdict("good", data.completed ? "🏁 Tamamlandı" : `DOĞRU ✓ — Adım ${(keyOf(data.nextStep) ?? data.currentStepId)}/${TOTAL_STEPS}`);
    // Doğru ilerleyince son uyarı temizlenir, panel olumlu duruma döner.
    aiState("ok", data.completed ? "🏁 Prosedür doğru tamamlandı." : `✓ Süreç doğru ilerliyor${next}`);
    toast("good", data.message || "Doğru aksiyon.");
    log("success", "✓ Doğru Aksiyon", data.message || "");
    relaxMonitor();
  }
  updateCounters();

  // Hatanın sonucu: bir koşul doğru oldu; dal ileride (karar adımında) açılacak ya da sonuç rapora yazıldı.
  // Sapma bildiriminden sonra gösterilir ki onun altında kalmasın; panoda dal açılana kadar durur.
  for (const a of (!replay && data.adaptive) || []) {
    const when = a.outcome ? "raporda görünecek" : `Adım ${a.evaluatedAt}'da akış dallanacak`;
    toast("bad", `Hatanın sonucu: ${a.title} — ${when}`);
    log("error", `⚠ Hatanın sonucu: ${a.title}`, `${a.message}${a.scenarioId ? `\nOnaylı senaryo: ${a.scenarioId}` : ""}\n${when}`);
    traffic("ai", "MOTOR", "src-aitable", "⚠", `Hata → ${a.condition}`, when);
    boardActive =
      `<div class="rx-card open sev-HIGH"><div class="rx-head"><span class="rx-badge">HATANIN SONUCU</span>` +
      `<span class="rx-sev">${escapeHtml(a.scenarioId ?? a.condition)}</span></div>` +
      `<div class="rx-title-main">${escapeHtml(a.title)}</div>` +
      `<div class="rx-narrative">${escapeHtml(a.message)}</div><div class="rx-note warn">${escapeHtml(when)}</div></div>`;
    paintBoard();
  }

  if (!replay) recordTimeline(sentType, data);

  if (!els.flowModal.classList.contains("hidden")) refreshFlow();

  if (data.completed) {
    setStep(null);
    markAllStepsDone();
    setVerdict("good", "🏁 Prosedür tamamlandı — 'Bitir & Rapor'");
    aiState("ok", "🏁 Prosedür doğru tamamlandı.");
    toast("good", "Prosedür tamamlandı! Raporu görün.");
  } else if (data.nextStep) {
    setStep(data.nextStep);
  } else if (data.activeComplication) {
    // Komplikasyon zinciri ilerledi: adım değişmez, görev kartı yenilenir.
    setStep(state.currentStep);
  }
}

// ---- Komplikasyon panosu: üstte aktif senaryo komplikasyonu, altta kümülatif risk ----
let boardActive = "";   // aktif komplikasyon kartı HTML'i
let boardCumulative = ""; // kümülatif risk listesi HTML'i

function paintBoard() {
  const html = boardActive + boardCumulative;
  els.compBoard.classList.toggle("empty", !html);
  els.compBoard.innerHTML = html ||
    `<p class="muted">Yanlış malzeme seçimiyle komplikasyon gelişirse, olay ve çözüm zinciri burada görünür.</p>`;
}

function renderActiveComplication(ac) {
  const rows = ac.actions.map((a) => `
    <div class="rx-step ${a.done ? "done" : (ac.currentAction && a.order === ac.currentAction.order ? "current" : "")}">
      <span class="rx-mark">${a.done ? "✔" : (ac.currentAction && a.order === ac.currentAction.order ? "▶" : "○")}</span>
      <span class="rx-body">
        <span class="rx-title">${a.order}. ${escapeHtml(a.title)}</span>
        <span class="rx-items">${(a.allowedInstruments || []).map((c) => escapeHtml(labelOf(c))).join(" / ")}${a.requiredSuture ? " + " + escapeHtml(sutureLabelOf(a.requiredSuture)) : ""}</span>
      </span>
    </div>`).join("");

  const badge = !ac.scrubResolvable ? "KAYDA GEÇTİ"
    : ac.resolved ? "GİDERİLDİ" : "AKTİF KOMPLİKASYON";
  boardActive =
    `<div class="rx-card ${!ac.scrubResolvable ? "irreversible" : ac.resolved ? "resolved" : "open"} sev-${escapeHtml(ac.severity)}">` +
      `<div class="rx-head"><span class="rx-badge">${badge}</span>` +
      `<span class="rx-sev">${escapeHtml(ac.severity)}</span></div>` +
      `<div class="rx-title-main">${escapeHtml(ac.title)}</div>` +
      `<div class="rx-narrative">${escapeHtml(ac.narrative)}</div>` +
      (ac.scrubResolvable
        ? `<div class="rx-note">✅ ${escapeHtml(ac.resolutionNote)}</div><div class="rx-list">${rows}</div>`
        : `<div class="rx-note warn">⛔ ${escapeHtml(ac.resolutionNote)}</div>`) +
    `</div>`;
  paintBoard();
}

// 60 adımlık modelde dal: kanama, yapışıklık, sayım uyuşmazlığı… Akışın kendisi çözüm adımlarını getirir.
const SERIOUS_BRANCHES = new Set(["BLEEDING_DETECTED", "BLEEDING_AFTER_IRRIGATION", "LIGATURE_NOT_SECURE", "COUNT_MISMATCH"]);

function handleBranch(b) {
  const serious = SERIOUS_BRANCHES.has(b.condition);
  if (!b.resolved) {
    state.openBranch = b;
    state.counters.complication++;
    toast(serious ? "bad" : "info", `DURUM — ${b.title}`);
    log(serious ? "error" : "info", `🩸 Dal: ${b.title}`, `Koşul: ${b.condition} (${b.source})\nAkış: Adım ${b.fromNodeId} → ${b.toNodeId}`);
    traffic("ai", "AKIŞ", "src-aitable", "⑂", b.title, `${b.fromNodeId} → ${b.toNodeId}`);
    if (serious) shockMonitor("MEDIUM");
  } else {
    state.openBranch = null;
    state.counters.resolved++;
    toast("good", `Yönetildi — ${b.title}`);
    log("success", "🩹 Dal tamamlandı", `${b.title} — ana akışa dönülüyor (Adım ${b.toNodeId ?? "-"})`);
    relaxMonitor();
  }
  renderBranch(b);
}

function renderBranch(b) {
  boardActive =
    `<div class="rx-card ${b.resolved ? "resolved" : "open"} sev-${SERIOUS_BRANCHES.has(b.condition) ? "HIGH" : "MEDIUM"}">` +
      `<div class="rx-head"><span class="rx-badge">${b.resolved ? "YÖNETİLDİ" : "AKIŞ DALI"}</span>` +
      `<span class="rx-sev">${escapeHtml(b.source)}</span></div>` +
      `<div class="rx-title-main">${escapeHtml(b.title)}</div>` +
      `<div class="rx-narrative">${b.resolved
        ? `Dal tamamlandı; ana akış Adım ${escapeHtml(b.toNodeId ?? "-")} ile sürüyor.`
        : `Akış Adım ${escapeHtml(b.fromNodeId)} → ${escapeHtml(b.toNodeId)} dalına girdi. Görev kartındaki adımları sırayla uygulayın.`}</div>` +
    `</div>`;
  paintBoard();
}

// ---- SignalR ----
async function connectHub() {
  try {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl()}/hubs/simulation`, { accessTokenFactory: () => auth.token })
      .withAutomaticReconnect().build();
    conn.on("AiFeedback", (fb) => {
      traffic(
        "ai",
        "AI·LLM",
        "src-aillm",
        "↺",
        "SignalR AiFeedback",
        `${fb.deviationType} · ${fb.possibleRisk || ""}`,
        fb.totalTokens ? `${fb.totalTokens} token` : ""
      );
      showAiFeedback(fb);
    });
    conn.on("ScoreUpdate", (u) => {
      if (u && typeof u.score === "number") {
        setScore(u.score);
        traffic("hub", "HUB", "src-hub", "↺", "SignalR ScoreUpdate", `skor=${u.score}`);
      }
    });
    conn.onreconnected(() => setConn(true));
    conn.onclose(() => setConn(false));
    await conn.start();
    await conn.invoke("JoinSession", state.sessionId);
    state.connection = conn;
    setConn(true);
  } catch (e) {
    setConn(false);
    log("error", "✕ Hub Hatası", formatError(e));
  }
}

async function disconnectHub() {
  if (state.connection) { try { await state.connection.stop(); } catch {} state.connection = null; }
  setConn(false);
}

function showAiFeedback(fb) {
  const sev = fb.severity || "MEDIUM";
  const isLlm = fb.source === "llm";
  const line = (k, v) => v ? `<div class="ai-line"><span class="k">${k}</span><span>${v}</span></div>` : "";
  const meta = isLlm
    ? `<span class="ai-call">🤖 AI çağrısı yapıldı</span>` +
      (fb.totalTokens ? `<span class="ai-tokens">${fb.modelName ? fb.modelName + " · " : ""}${fb.totalTokens} token</span>` : "")
    : `<span class="ai-src">Kaynak: klinik tablo</span>`;
  els.aiCard.classList.remove("empty");
  els.aiCard.innerHTML =
    `<div class="ai-head"><span class="sev sev-${sev}">${sev}</span>` +
    `<span class="ai-dev">${fb.deviationType ?? ""}</span>` + meta + `</div>` +
    line("Olası Risk", fb.possibleRisk) +
    line("Açıklama", fb.explanation) +
    line("Önerilen Müdahale", fb.recommendedAction);
  log("ai", "🧠 AI Geri Bildirim", `${fb.deviationType}\nRisk: ${fb.possibleRisk}\nAçıklama: ${fb.explanation}\nMüdahale: ${fb.recommendedAction}`);
}

// Panel olumlu/nötr duruma döner (doğru ilerleme veya ipucu) — son uyarı kalıcı olmaz.
function aiState(kind, text) {
  els.aiCard.classList.remove("empty");
  els.aiCard.innerHTML = `<div class="ai-state ai-${kind}">${text}</div>`;
}

function aiIdle() {
  els.aiCard.classList.add("empty");
  els.aiCard.innerHTML = `<p class="muted">Bir hata yaptığınızda, olası klinik komplikasyon ve doğru müdahale açıklaması burada belirir.</p>`;
}

// ---- Sahne: elde tutma / teslim ----
function pickUp(code) {
  if (!state.sessionId) { toast("bad", "Önce seansı başlatın."); return; }
  state.holding = code;
  const ins = INSTRUMENT_CATALOG.find((i) => i.code === code);
  els.handHolding.textContent = `${ins?.icon ?? ""} ${ins?.name ?? code}`;
  els.handHolding.classList.remove("empty");
  els.releaseBtn.disabled = false;
  els.instrumentGrid.querySelectorAll(".tray-card").forEach((c) =>
    c.classList.toggle("held", c.dataset.code === code));
  document.querySelectorAll(".zone").forEach((z) => z.classList.add("armed"));
  // Sütür gereken adımda doğru sütürü öner.
  const step = stepMeta(keyOf(state.currentStep));
  const isHolder = code === "NEEDLE_HOLDER" || ins?.category === "needle_holder";
  if (step?.suture && isHolder) els.sutureSelect.value = step.suture;
}

function release() {
  state.holding = null;
  els.handHolding.textContent = "— (tepsiden bir alet seçin) —";
  els.handHolding.classList.add("empty");
  els.releaseBtn.disabled = true;
  els.instrumentGrid.querySelectorAll(".tray-card.held").forEach((c) => c.classList.remove("held"));
  document.querySelectorAll(".zone").forEach((z) => z.classList.remove("armed", "drag-over"));
}

function deliverToZone(zone) {
  if (!state.holding) { toast("bad", "Önce tepsiden bir alet seçin/sürükleyin."); return; }
  const map = ZONE_MAP[zone];
  const fields = { eventType: map.eventType, instrumentCode: state.holding, target: map.target };
  const step = stepMeta(keyOf(state.currentStep));
  const act = state.activeComplication && !state.activeComplication.resolved
    ? state.activeComplication.currentAction : null;
  const suture = els.sutureSelect.value;
  // 60 adımlık modelde iplik yalnızca seçici açıkken (adım iplik istiyorsa) gönderilir.
  const sutureOpen = !els.sutureWrap.classList.contains("hidden");
  if (suture && (isFlowModel() ? sutureOpen : (act?.requiredSuture || step?.suture || state.holding === "NEEDLE_HOLDER"))) fields.sutureCode = suture;
  sendEvent(fields);
  release();
}

function autoDeliver() {
  const step = state.currentStep;
  if (!step) { toast("bad", "Aktif adım yok."); return; }

  // Komplikasyon açıkken doğru cevap, çözüm zincirinin sıradaki malzemesidir.
  const act = state.activeComplication && !state.activeComplication.resolved
    ? state.activeComplication.currentAction : null;
  if (act) {
    const map = ZONE_MAP[act.target || "SURGEON"];
    const fields = { eventType: map.eventType, instrumentCode: act.allowedInstruments[0], target: map.target };
    if (act.requiredSuture) fields.sutureCode = act.requiredSuture;
    sendEvent(fields);
    release();
    return;
  }

  const meta = stepMeta(keyOf(step));
  if ((step.expectedEvent ?? meta?.event) === "count_confirmed") {
    if (isFlowModel() && step.countCategory) setCountInput(step.countCategory, step.tableCount);
    sendCount();
    return;
  }
  if ((step.expectedEvent ?? meta?.event) === "step_confirmed") { sendEvent({ eventType: "step_confirmed" }); return; }
  if (step.expectedEvent === "checklist_confirmed") {
    els.checklistItems.querySelectorAll("input").forEach((c) => { c.checked = true; });
    sendChecklist();
    return;
  }
  // İpliğin tek başına hazırlandığı adımda (ör. 3/0 ipek) iplik doğrudan uzatılır.
  const code = (step.allowedInstruments?.length ? step.allowedInstruments : meta?.allowed || [])[0] ?? (isFlowModel() ? step.requiredSuture : undefined);
  if (!code) { toast("bad", "Bu adımda izinli alet yok."); return; }
  const map = ZONE_MAP[meta?.target || "SURGEON"];
  const fields = { eventType: map.eventType, instrumentCode: code, target: map.target };
  const suture = step.requiredSuture || meta?.suture;
  if (suture && suture !== code) fields.sutureCode = suture;
  sendEvent(fields);
  release();
}

// Ameliyat öncesi kontrol listesi: işaretlenen madde kimlikleri gönderilir.
function sendChecklist() {
  const checkedItems = [...els.checklistItems.querySelectorAll("input:checked")].map((c) => c.value);
  sendEvent({ eventType: "checklist_confirmed", checkedItems });
}

function renderChecklist(step) {
  if (els.checklistItems.dataset.node === keyOf(step)) return;   // aynı adımda işaretler korunur
  els.checklistItems.dataset.node = keyOf(step);
  els.checklistItems.innerHTML = (step.checklist || []).map((i) =>
    `<label><input type="checkbox" value="${escapeHtml(i.id)}" />` +
    `<span><span class="ci-name">${escapeHtml(i.name)}</span>${i.optional ? `<span class="ci-opt">gerekiyorsa</span>` : ""}` +
    `${i.task ? `<br><span class="ci-task">${escapeHtml(i.task)}</span>` : ""}</span></label>`).join("");
}

const COUNT_INPUT = () => ({ COMPRESS: els.cntCompress, INSTRUMENT: els.cntInstrument, NEEDLE: els.cntNeedle });
function setCountInput(category, value) { const el = COUNT_INPUT()[category]; if (el) el.value = value; }

function sendCount() {
  const cat = state.currentStep?.countCategory;
  // 60 adımlık modelde her sayım ayrı adımdır: yalnızca o kategorinin sayısı gönderilir.
  const counts = isFlowModel() && cat
    ? { [cat]: Number(COUNT_INPUT()[cat].value) }
    : { COMPRESS: Number(els.cntCompress.value), INSTRUMENT: Number(els.cntInstrument.value), NEEDLE: Number(els.cntNeedle.value) };
  sendEvent({ eventType: "count_confirmed", counts });
}

// ---- UI güncelleyiciler ----
function setScore(score) {
  if (typeof score !== "number") return;
  els.scoreValue.textContent = score;
  const hue = Math.max(0, Math.min(120, (score / 100) * 120));
  els.scoreValue.style.color = `hsl(${hue}, 75%, 58%)`;
}

function setStep(step) {
  state.currentStep = step;
  if (!step) {
    els.progressLabel.textContent = `Adım — / ${TOTAL_STEPS}`;
    els.taskCard.classList.add("empty");
    els.taskCard.innerHTML = `<p class="muted">Aktif görev yok.</p>`;
    updateSceneMode(null);
    return;
  }
  const id = keyOf(step);
  const meta = stepMeta(id);
  const phaseNo = step.phaseNo || meta?.phaseNo || 0;
  const phaseTxt = phaseNo ? `Aşama ${phaseNo} / ${PHASE_COUNT}` : `${escapeHtml(step.phase ?? "Alt akış")}`;
  els.progressLabel.textContent = `${phaseTxt} · Adım ${id}/${TOTAL_STEPS}`;
  els.progressBar.style.width = `${(stepNoOf(step) / TOTAL_STEPS) * 100}%`;

  // Komplikasyon açıkken görev kartı çözüm zincirinin sıradaki malzemesini gösterir.
  if (state.activeComplication && !state.activeComplication.resolved && state.activeComplication.currentAction) {
    renderComplicationTask(state.activeComplication);
    updateSceneMode(step);
    updateStepList(id);
    return;
  }

  const allowed = step.allowedInstruments?.length ? step.allowedInstruments : (meta?.allowed || []);
  const sutures = step.requiredSutures?.length ? step.requiredSutures : ((step.requiredSuture || meta?.suture) ? [step.requiredSuture || meta?.suture] : []);
  const clamps = step.clamps?.length ? step.clamps : null;
  const branch = step.isBranch && (step.conditionText || state.openBranch?.title);
  els.taskCard.classList.remove("empty");
  els.taskCard.classList.toggle("complication", !!branch);
  const chips = clamps
    ? clamps.map((c) => `<span class="chip hi">${c.qty}× ${escapeHtml(labelOf(c.code))}</span>`).join("")
    : allowed.map((c) => `<span class="chip hi">${escapeHtml(labelOf(c))}</span>`).join("");
  els.taskCard.innerHTML =
    `<div class="ts-badge ${branch ? "danger" : ""}">${phaseNo ? `Aşama ${phaseNo}` : "Dal"}</div>` +
    `<div class="ts-body">` +
      `<div class="ts-title">${branch ? "⑂ " : ""}${escapeHtml(isFlowModel() ? `${id}. ` : "")}${escapeHtml(step.title ?? meta?.title ?? "")}</div>` +
      (branch ? `<div class="ts-inst">🩸 ${escapeHtml(branch)}</div>` : "") +
      `<div class="ts-inst">${escapeHtml(step.instruction ?? meta?.instruction ?? "")}</div>` +
    `</div>` +
    `<div class="ts-chips">${chips}` +
    `${sutures.map((x) => `<span class="chip su">${escapeHtml(sutureLabelOf(x))}</span>`).join("")}` +
    `${allowed.length || sutures.length ? "" : `<span class='muted'>${step.kind === "count" ? "sayım" : "yeni alet yok"}</span>`}</div>`;

  updateSceneMode(step);
  updateStepList(id);
}

// Görev kartını komplikasyon çözüm moduna alır.
function renderComplicationTask(ac) {
  const a = ac.currentAction;
  els.taskCard.classList.remove("empty");
  els.taskCard.classList.add("complication");
  els.taskCard.innerHTML =
    `<div class="ts-badge danger">${ac.resolutionIndex + 1}/${ac.resolutionTotal}</div>` +
    `<div class="ts-body">` +
      `<div class="ts-title">🩸 ${escapeHtml(ac.title)} — ${escapeHtml(a.title)}</div>` +
      `<div class="ts-inst">${escapeHtml(a.note || "")}</div>` +
    `</div>` +
    `<div class="ts-chips">${(a.allowedInstruments || []).map((c) => `<span class="chip hi">${escapeHtml(labelOf(c))}</span>`).join("")}` +
    `${a.requiredSuture ? `<span class="chip su">${escapeHtml(sutureLabelOf(a.requiredSuture))}</span>` : ""}</div>`;
}

function labelOf(code) {
  return INSTRUMENT_CATALOG.find((i) => i.code === code)?.name ?? code;
}

function sutureLabelOf(code) {
  return SUTURE_CATALOG.find((s) => s.code === code)?.name ?? code;
}

function updateSceneMode(step) {
  const meta = step ? stepMeta(keyOf(step)) : null;
  const ac = state.activeComplication;
  const act = ac && !ac.resolved ? ac.currentAction : null;
  const event = step?.expectedEvent ?? meta?.event;
  const isCount = !act && event === "count_confirmed";
  const isConfirm = !act && event === "step_confirmed";
  const isChecklist = !act && event === "checklist_confirmed";
  els.checklistMode.classList.toggle("hidden", !isChecklist);
  if (isChecklist) renderChecklist(step);
  els.countMode.classList.toggle("hidden", !isCount);
  els.trayWrap.classList.toggle("dim", isCount || isConfirm || isChecklist);
  els.confirmBtn.classList.toggle("hidden", !isFlowModel() || !step);
  els.confirmBtn.classList.toggle("btn-primary", isConfirm);
  els.confirmBtn.classList.toggle("btn-ghost", !isConfirm);

  if (isCount && isFlowModel() && step.countCategory) {
    // Her sayım ayrı adım: yalnızca o kategori; kutu başlangıç kaydıyla gelir, masadaki gerçek sayı girilir.
    els.countMode.querySelectorAll("[data-count]").forEach((f) => f.classList.toggle("hidden", f.dataset.count !== step.countCategory));
    setCountInput(step.countCategory, step.recordedCount);
    const name = { COMPRESS: "kompres", INSTRUMENT: "alet", NEEDLE: "iğne" }[step.countCategory];
    els.countHelp.innerHTML = `Masada sayılan ${name}: <b>${step.tableCount}</b> · başlangıç kaydı: <b>${step.recordedCount}</b>. ` +
      `Sirküleyle birlikte sayıp <b>masadaki gerçek sayıyı</b> girin; uyuşmazlık varsa doğrulamada bildirilir.`;
  } else if (isCount && meta?.counts) {
    els.countMode.querySelectorAll("[data-count]").forEach((f) => f.classList.remove("hidden"));
    els.cntCompress.value = meta.counts.COMPRESS;
    els.cntInstrument.value = meta.counts.INSTRUMENT;
    els.cntNeedle.value = meta.counts.NEEDLE;
  }

  // Sütür seçici görünürlüğü (komplikasyon açıkken zincirin kendi sütürü geçerlidir).
  const requiredSuture = act ? act.requiredSuture : (step?.requiredSuture ?? meta?.suture);
  els.sutureWrap.classList.toggle("hidden", !requiredSuture);
  if (requiredSuture) els.sutureSelect.value = requiredSuture;

  // Beklenen alet vurgusu; "yalnızca beklenen" filtresi açıksa ray yeniden kurulur.
  const allowed = new Set(act
    ? (act.allowedInstruments || [])
    : (step ? (step.allowedInstruments?.length ? step.allowedInstruments : meta?.allowed || []) : []));
  if (trayFilter.expectedOnly) buildTray();
  else highlightExpected(allowed);

  // Beklenen bölge vurgusu.
  const zone = act ? (act.target || "SURGEON") : (meta?.target || "SURGEON");
  document.querySelectorAll(".zone").forEach((z) =>
    z.classList.toggle("expected", !isCount && !isConfirm && !isChecklist && z.dataset.zone === zone));
}

function setVerdict(kind, text) {
  els.verdictBanner.textContent = text;
  els.verdictBanner.className = `eval-verdict ev-${kind}`;
}

function resetCounters() {
  state.visited = new Set();
  state.openBranch = null;
  state.counters = { correct: 0, wrong: 0, sterile: 0, hint: 0, complication: 0, resolved: 0 };
  state.activeComplication = null;
  state.timeline = [];
  updateCounters();
  els.aiCard.classList.add("empty");
  els.aiCard.innerHTML = `<p class="muted">Bir hata yaptığınızda, olası klinik komplikasyon ve doğru müdahale açıklaması burada belirir.</p>`;
  resetComplications();
  baselineVitals();
  setMonitorLevel("STABLE");
}

// ---- Kümülatif komplikasyon riski (aktif komplikasyon kartının altında) ----
function renderComplications(assess) {
  if (!assess || !assess.items || assess.items.length === 0) { boardCumulative = ""; paintBoard(); return; }
  const rows = assess.items.map((it) => `
    <div class="comp-item ${it.primary ? "primary" : ""}">
      <div class="comp-row">
        <span class="comp-dev">${escapeHtml(it.deviationType)}</span>
        ${it.primary ? '<span class="comp-tag">EN OLASI</span>' : ""}
        <span class="comp-prob">%${it.probability}</span>
      </div>
      <div class="comp-risk">${escapeHtml(it.risk)}</div>
      <div class="comp-bar"><span style="width:${it.probability}%"></span></div>
      <div class="comp-meta">${it.occurrences}× tekrar · şiddet ${escapeHtml(it.severity)}</div>
    </div>`).join("");
  boardCumulative =
    `<div class="comp-cum"><div class="comp-summary">${escapeHtml(assess.summary)}</div>` +
    `<div class="comp-list">${rows}</div></div>`;
  paintBoard();
}

function resetComplications() {
  if (!els.compBoard) return;
  boardActive = "";
  boardCumulative = "";
  paintBoard();
}

// Komplikasyon anında hasta monitörünü sert biçimde sarsar.
function shockMonitor(severity) {
  const bump = { LOW: 10, MEDIUM: 20, HIGH: 32, CRITICAL: 46 }[severity] || 24;
  monitor.stress = clamp(monitor.stress + bump, 0, 100);
  monitor.currentRiskScore = clamp(monitor.currentRiskScore + bump, 0, 99);
  monitor.targetHeartRate = clamp(76 + monitor.stress * 0.95, 70, 178);
  monitor.targetPulseRate = clamp(monitor.targetHeartRate - 2 - (monitor.stress > 60 ? 8 : 0), 55, 175);
  monitor.targetRespRate = clamp(14 + monitor.stress * 0.25, 12, 42);
  setMonitorLevel(severity === "CRITICAL" ? "CRITICAL" : "HIGH", severity);
}

// ---- Hasta monitörü: canlı vital simülasyonu ----
function baselineVitals() {
  monitor.stress = 0;
  monitor.currentRiskScore = 0;
  monitor.targetHeartRate = 76;
  monitor.targetPulseRate = 74;
  monitor.targetRespRate = 14;
}

function applyDeviationToMonitor(assess, severity) {
  const risk = assess?.riskScore ?? 0;
  monitor.currentRiskScore = risk;
  const bump = { LOW: 6, MEDIUM: 12, HIGH: 22, CRITICAL: 34 }[severity] || 12;
  monitor.stress = clamp(Math.max(monitor.stress, risk) + bump, 0, 100);
  monitor.targetHeartRate = clamp(76 + monitor.stress * 0.9, 70, 175);
  // Aşırı stres altında nabız açığı (pulse deficit) belirir.
  monitor.targetPulseRate = clamp(monitor.targetHeartRate - 2 - (monitor.stress > 60 ? 6 : 0), 58, 172);
  monitor.targetRespRate = clamp(14 + monitor.stress * 0.22, 12, 40);
  setMonitorLevel(assess?.level, severity);
}

function relaxMonitor() {
  monitor.stress = clamp(monitor.stress - 22, 0, 100);
  monitor.currentRiskScore = clamp(monitor.currentRiskScore - 25, 0, 100);
  monitor.targetHeartRate = clamp(76 + monitor.stress * 0.9, 70, 175);
  monitor.targetPulseRate = clamp(monitor.targetHeartRate - 2, 58, 172);
  monitor.targetRespRate = clamp(14 + monitor.stress * 0.22, 12, 40);
  setMonitorLevel(monitor.stress > 55 ? "HIGH" : monitor.stress > 20 ? "ELEVATED" : "STABLE");
}

function setMonitorLevel(level, severity) {
  const cls = level === "CRITICAL" || severity === "CRITICAL" ? "critical"
    : level === "HIGH" ? "high"
    : level === "ELEVATED" || level === "MEDIUM" ? "elevated"
    : "stable";
  const mm = $("miniMonitor");
  if (mm) mm.className = `mini-monitor ${cls}`;
}

// PQRST benzeri tek atım dalga biçimi (faz 0..1).
function ecgSample(phase) {
  const t = phase - Math.floor(phase);
  const g = (c, w, a) => a * Math.exp(-Math.pow((t - c) / w, 2));
  return g(0.16, 0.03, 0.10) + g(0.26, 0.012, -0.15) + g(0.30, 0.012, 1.0) + g(0.34, 0.014, -0.25) + g(0.58, 0.05, 0.22);
}

function startMonitor() {
  if (monitor.timerId || !els.vitalWave) return;
  const canvas = els.vitalWave;
  const ctx = canvas.getContext("2d");
  const W = canvas.width, H = canvas.height;
  const wave = new Array(W).fill(H / 2);
  const sps = 240; // sabit tarama hızı (px/sn)
  monitor.lastTick = performance.now();

  function frame(now) {
    const dt = Math.min(0.05, (now - monitor.lastTick) / 1000);
    monitor.lastTick = now;
    const ease = Math.min(1, dt * 2.5);
    monitor.heartRate += (monitor.targetHeartRate - monitor.heartRate) * ease;
    monitor.pulseRate += (monitor.targetPulseRate - monitor.pulseRate) * ease;
    monitor.respRate += (monitor.targetRespRate - monitor.respRate) * ease;
    monitor.stress = clamp(monitor.stress - dt * 3, 0, 100);
    monitor.currentRiskScore = clamp(monitor.currentRiskScore - dt * 4, 0, 100);

    const steps = Math.max(1, Math.round(dt * sps));
    for (let i = 0; i < steps; i++) {
      monitor.wavePhase += (1 / sps) * (monitor.heartRate / 60);
      wave.push(H / 2 - ecgSample(monitor.wavePhase) * (H * 0.42));
      wave.shift();
    }
    drawEcg(ctx, W, H, wave);
    updateMonitorUi();
    monitor.timerId = requestAnimationFrame(frame);
  }
  monitor.timerId = requestAnimationFrame(frame);
}

function drawEcg(ctx, W, H, wave) {
  ctx.clearRect(0, 0, W, H);
  ctx.strokeStyle = "rgba(45,212,191,0.10)";
  ctx.lineWidth = 1;
  for (let x = 0; x < W; x += 14) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, H); ctx.stroke(); }
  for (let y = 0; y < H; y += 14) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke(); }
  const color = monitor.stress > 70 ? "#fb7185" : monitor.stress > 35 ? "#fcd34d" : "#34d399";
  ctx.strokeStyle = color;
  ctx.lineWidth = 1.6;
  ctx.lineJoin = "round";
  ctx.shadowColor = color;
  ctx.shadowBlur = 6;
  ctx.beginPath();
  const stepX = W / wave.length;
  for (let i = 0; i < wave.length; i++) {
    const x = i * stepX;
    i ? ctx.lineTo(x, wave[i]) : ctx.moveTo(x, wave[i]);
  }
  ctx.stroke();
  ctx.shadowBlur = 0;
}

function updateMonitorUi() {
  if (els.hrValue) els.hrValue.textContent = Math.round(monitor.heartRate);
  if (els.respValue) els.respValue.textContent = Math.round(monitor.respRate);
  if (els.riskValue) els.riskValue.textContent = `${Math.round(monitor.currentRiskScore)}%`;
  if (els.pulseValue) els.pulseValue.textContent = Math.round(monitor.pulseRate);
}

// ---- Nabız sesi (WebAudio: HR'ye senkron "lub-dub" kalp atışı) ----
function ensureAudio() {
  if (monitor.audioCtx) return;
  const AC = window.AudioContext || window.webkitAudioContext;
  if (!AC) return;
  const ctx = new AC();
  const master = ctx.createGain();
  master.gain.value = 0.9;
  master.connect(ctx.destination);
  monitor.audioCtx = ctx;
  monitor.masterGain = master;
  monitor.audioReady = true;
}

// Tek perküsif kalp vuruşu: alçak sinüs + hızlı sönüm (frekans düşüşü "thump" hissi verir).
function thump(t0, freq, peak, dur) {
  const ctx = monitor.audioCtx;
  const osc = ctx.createOscillator();
  osc.type = "sine";
  osc.frequency.setValueAtTime(freq, t0);
  osc.frequency.exponentialRampToValueAtTime(freq * 0.55, t0 + dur);
  const g = ctx.createGain();
  g.gain.setValueAtTime(0.0001, t0);
  g.gain.exponentialRampToValueAtTime(peak, t0 + 0.012);
  g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
  osc.connect(g); g.connect(monitor.masterGain);
  osc.start(t0);
  osc.stop(t0 + dur + 0.03);
}

function scheduleHeartbeat() {
  if (!monitor.audioReady || !monitor.audioEnabled) return;
  const ctx = monitor.audioCtx;
  const t = ctx.currentTime + 0.02;
  const bpm = clamp(monitor.heartRate, 40, 200);
  const period = 60 / bpm;
  const amp = clamp(0.24 + (monitor.stress / 100) * 0.34, 0.24, 0.6);
  thump(t, 54, amp, 0.16);                                   // S1 "lub"
  thump(t + Math.min(0.16, period * 0.30), 44, amp * 0.7, 0.13); // S2 "dub"
  clearTimeout(monitor.beatTimer);
  monitor.beatTimer = setTimeout(scheduleHeartbeat, period * 1000);
}

function startHeartbeat() {
  if (!monitor.audioReady || !monitor.audioEnabled) return;
  if (monitor.audioCtx?.state === "suspended") monitor.audioCtx.resume();
  scheduleHeartbeat();
}

function stopHeartbeat() {
  clearTimeout(monitor.beatTimer);
}

function toggleAudio() {
  monitor.audioEnabled = !monitor.audioEnabled;
  els.audioToggle.classList.toggle("off", !monitor.audioEnabled);
  els.audioToggle.textContent = monitor.audioEnabled ? "🔊" : "🔇";
  if (monitor.audioEnabled) { ensureAudio(); startHeartbeat(); }
  else { stopHeartbeat(); }
}

function updateCounters() {
  els.cntCorrect.textContent = state.counters.correct;
  els.cntWrong.textContent = state.counters.wrong;
  els.cntSterile.textContent = state.counters.sterile;
  els.cntHint.textContent = state.counters.hint;
  if (els.cntComp) els.cntComp.textContent = state.counters.complication;
  if (els.cntResolved) els.cntResolved.textContent = state.counters.resolved;
}

// ---- Kontrol listesi: 22 ana aşama başlığı, altında o aşamanın adımları ----
function buildStepList() {
  els.stepList.innerHTML = "";
  let lastPhase = null;
  for (const s of STEPS) {
    const phaseKey = `${s.phaseNo}|${s.phase}`;
    if (phaseKey !== lastPhase) {
      lastPhase = phaseKey;
      const head = document.createElement("li");
      const preop = s.kind === "checklist";
      head.className = "phase-head" + (s.phaseNo || preop ? "" : " branch");
      head.dataset.phase = s.phaseNo;
      head.innerHTML = `<span class="phase-no">${s.phaseNo || (preop ? "Ö" : "⑂")}</span><span class="phase-nm">${escapeHtml(s.phase)}</span>`;
      els.stepList.appendChild(head);
    }
    const multi = STEPS.filter((x) => x.phaseNo === s.phaseNo).length > 1;
    const li = document.createElement("li");
    li.className = "step-item pending" + (multi ? " sub" : "") + (s.isBranch ? " branch" : "");
    li.dataset.id = s.id;
    li.dataset.phase = s.phaseNo;
    const label = isFlowModel() ? `${s.id}. ${s.title}` : s.title;
    li.innerHTML = `<span class="step-mark">○</span><span class="step-txt">${escapeHtml(label)}</span>`;
    els.stepList.appendChild(li);
  }
}

function updateStepList(currentId) {
  const cur = stepMeta(currentId);
  if (isFlowModel()) {
    // Akış dallandığı için sıra değil, gerçekten yürünen düğümler işaretlenir; atlanan dallar soluk kalır.
    const curIdx = STEPS.findIndex((s) => s.id === currentId);
    els.stepList.querySelectorAll(".step-item").forEach((li, i) => {
      const id = li.dataset.id;
      li.classList.remove("done", "active", "pending", "skipped");
      const mark = li.querySelector(".step-mark");
      if (id === currentId) { li.classList.add("active"); mark.textContent = "▶"; }
      else if (state.visited.has(id)) { li.classList.add("done"); mark.textContent = "✔"; }
      else if (i < curIdx) { li.classList.add("skipped"); mark.textContent = "–"; }
      else { li.classList.add("pending"); mark.textContent = "○"; }
    });
    els.stepList.querySelectorAll(".phase-head").forEach((h) => {
      const no = Number(h.dataset.phase);
      h.classList.toggle("active", !!cur && no === cur.phaseNo && no > 0);
      h.classList.toggle("done", !!cur && no > 0 && cur.phaseNo > 0 && no < cur.phaseNo);
    });
    revealInList(els.stepList.querySelector(".step-item.active"));
    return;
  }
  els.stepList.querySelectorAll(".step-item").forEach((li) => {
    const id = Number(li.dataset.id);
    li.classList.remove("done", "active", "pending");
    if (id < currentId) { li.classList.add("done"); li.querySelector(".step-mark").textContent = "✔"; }
    else if (id === currentId) { li.classList.add("active"); li.querySelector(".step-mark").textContent = "▶"; }
    else { li.classList.add("pending"); li.querySelector(".step-mark").textContent = "○"; }
  });
  els.stepList.querySelectorAll(".phase-head").forEach((h) => {
    const no = Number(h.dataset.phase);
    h.classList.toggle("active", !!cur && no === cur.phaseNo);
    h.classList.toggle("done", !!cur && no < cur.phaseNo);
  });
  revealInList(els.stepList.querySelector(".step-item.active"));
}

// Aktif adımı yalnızca listenin kendi kaydırma kutusunda görünür kılar; sayfanın kendisi kaymaz.
function revealInList(item) {
  if (!item) return;
  let box = item.parentElement;
  while (box && box !== document.body && !(box.scrollHeight > box.clientHeight && getComputedStyle(box).overflowY !== "visible"))
    box = box.parentElement;
  if (!box || box === document.body) return;
  const top = item.getBoundingClientRect().top - box.getBoundingClientRect().top + box.scrollTop;
  if (top < box.scrollTop || top + item.offsetHeight > box.scrollTop + box.clientHeight)
    box.scrollTo({ top: Math.max(0, top - box.clientHeight / 2), behavior: "smooth" });
}

function markAllStepsDone() {
  els.stepList.querySelectorAll(".step-item").forEach((li) => {
    li.classList.remove("active", "pending"); li.classList.add("done");
    li.querySelector(".step-mark").textContent = "✔";
  });
  els.stepList.querySelectorAll(".phase-head").forEach((h) => {
    h.classList.remove("active"); h.classList.add("done");
  });
  els.progressBar.style.width = "100%";
}

// ---- Rapor ----
function showReport(report, events) {
  els.reportUser.textContent = `— kullanıcı: ${auth.user?.username ?? "-"}`;
  const rows = [
    ["Skor", `${report.score}/100`],
    ["Başarı Oranı", report.successRate != null ? `%${report.successRate}` : "-"],
    ["Doğru Aksiyon", report.correctActions],
    ["Yanlış Aksiyon", report.wrongActions],
    ["Steril İhlali", report.sterileViolations],
    ["Kullanılan İpucu", report.hintsUsed],
    ["Gelişen Komplikasyon", report.complicationsTriggered ?? 0],
    ["Giderilen Komplikasyon", report.complicationsResolved ?? 0],
  ];
  els.reportBody.innerHTML =
    rows.map(([k, v]) => `<div class="report-row"><span class="k">${k}</span><span class="v">${v ?? "-"}</span></div>`).join("") +
    (report.summary ? `<div class="report-summary">${escapeHtml(report.summary)}</div>` : "");
  drawReportChart();
  renderFlow(events);
  els.reportModal.classList.remove("hidden");
}

// Vital seyri kaydı (her geçerli olayda hedef nabız/solunum + risk).
function recordTimeline(type, data) {
  const label = data.deviation ? `SAPMA ${data.deviation.deviationType}`
    : type === "hint_requested" ? "İpucu"
    : data.completed ? "Tamamlandı"
    : `Adım ${data.currentNodeId ?? data.currentStepId} ✓`;
  state.timeline.push({
    hr: Math.round(monitor.targetHeartRate),
    resp: Math.round(monitor.targetRespRate),
    risk: Math.round(monitor.currentRiskScore),
    ok: !data.deviation && type !== "hint_requested",
    label,
  });
}

function drawReportChart() {
  const c = els.reportChart, ctx = c.getContext("2d");
  const W = c.width, H = c.height, pad = 26;
  ctx.clearRect(0, 0, W, H);
  ctx.fillStyle = "#0a1120"; ctx.fillRect(0, 0, W, H);
  ctx.strokeStyle = "#22304f"; ctx.lineWidth = 1;
  for (let i = 0; i <= 4; i++) { const y = pad + (H - 2 * pad) * i / 4; ctx.beginPath(); ctx.moveTo(pad, y); ctx.lineTo(W - pad, y); ctx.stroke(); }
  const tl = state.timeline;
  if (!tl.length) { ctx.fillStyle = "#8a99c0"; ctx.font = "12px sans-serif"; ctx.fillText("Bu seansta veri kaydı yok.", W / 2 - 70, H / 2); return; }
  const n = tl.length;
  const xAt = (i) => pad + (W - 2 * pad) * (n === 1 ? 0.5 : i / (n - 1));
  const yAt = (v) => H - pad - (H - 2 * pad) * clamp(v, 0, 1);
  const series = [
    { key: "hr", color: "#f87171", max: 200 },
    { key: "resp", color: "#60a5fa", max: 40 },
    { key: "risk", color: "#fbbf24", max: 100 },
  ];
  for (const s of series) {
    ctx.strokeStyle = s.color; ctx.lineWidth = 2; ctx.lineJoin = "round"; ctx.beginPath();
    tl.forEach((p, i) => { const x = xAt(i), y = yAt(p[s.key] / s.max); i ? ctx.lineTo(x, y) : ctx.moveTo(x, y); });
    ctx.stroke();
    // Sapma noktalarını vurgula.
    tl.forEach((p, i) => { if (!p.ok) { ctx.fillStyle = s.color; ctx.beginPath(); ctx.arc(xAt(i), yAt(p[s.key] / s.max), 2.6, 0, 7); ctx.fill(); } });
  }
  els.reportChartLegend.innerHTML =
    '<span class="lg-hr">● Nabız (bpm)</span><span class="lg-resp">● Solunum (/dk)</span><span class="lg-risk">● Risk (%)</span>';
}

function renderFlow(events) {
  if (!events || !events.length) { els.reportFlow.innerHTML = '<p class="muted">Kayıt yok.</p>'; return; }
  els.reportFlow.innerHTML = events.map((e, i) => {
    const ok = e.isSuccess;
    const ins = e.instrumentCode ? ` · ${labelOf(e.instrumentCode)}` : "";
    const dev = e.deviationType ? ` · ${e.deviationType}` : "";
    const sc = typeof e.scoreChange === "number" ? `${e.scoreChange > 0 ? "+" : ""}${e.scoreChange}` : "";
    return `<div class="flow-row ${ok ? "ok" : "bad"}">` +
      `<span class="fr-idx">${i + 1}</span>` +
      `<span class="fr-mark">${ok ? "✓" : "✕"}</span>` +
      `<span class="fr-main">Adım ${escapeHtml(e.nodeId ?? e.stepId)} · ${escapeHtml(e.eventType)}${escapeHtml(ins)}${escapeHtml(dev)}</span>` +
      `<span class="fr-score">${sc}</span></div>`;
  }).join("");
}

// ---- Genel ----
function setConn(on) {
  els.connState.textContent = on ? "● Canlı" : "● Bağlı değil";
  els.connState.className = `conn-badge ${on ? "conn-on" : "conn-off"}`;
}

function formatError(e) {
  return e.data ? `${e.message}\n${JSON.stringify(e.data, null, 2)}` : (e.message || String(e));
}

// ---- Kurulum ----
// Tepsi kategoriye göre gruplanır; set kalemleri ürün koduyla, sarf malzemeler "EK" rozetiyle görünür.
// Yerleşim: sabit yükseklikli, yatay kaydırılan iki satırlık ray — bırakma bölgeleri hep görünür kalır.
const CATEGORY_ORDER = ["Kesici", "Makas", "Penset", "Klemp", "Ekartör", "Portegü", "Cihaz Ucu", "Cihaz", "Solüsyon", "Sarf", "Örtü", "Kap", "Konteyner",
  "blade", "handle", "scissors", "forceps", "clamp", "retractor", "needle_holder", "energy", "suction", "syringe", "fluid", "antiseptic", "sponge", "dressing", "container"];
const trayFilter = { text: "", category: "ALL", expectedOnly: false };

function categoriesInOrder() {
  const cats = [...new Set(INSTRUMENT_CATALOG.map((i) => i.category || "Diğer"))];
  return cats.sort((a, b) => {
    const ia = CATEGORY_ORDER.indexOf(a), ib = CATEGORY_ORDER.indexOf(b);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
  });
}

function buildTrayChips() {
  if (!els.trayChips) return;
  const cats = ["ALL", ...categoriesInOrder()];
  els.trayChips.innerHTML = cats.map((c) =>
    `<button class="tcat ${c === trayFilter.category ? "on" : ""}" data-cat="${escapeHtml(c)}">` +
    `${c === "ALL" ? "Tümü" : escapeHtml(catLabel(c))}</button>`).join("");
  els.trayChips.querySelectorAll(".tcat").forEach((b) =>
    b.addEventListener("click", () => {
      trayFilter.category = b.dataset.cat;
      trayFilter.expectedOnly = false;
      buildTrayChips();
      buildTray();
    }));
}

// O anda beklenen malzeme kodları (komplikasyon açıksa zincirin sıradaki basamağı).
function expectedCodes() {
  const ac = state.activeComplication;
  if (ac && !ac.resolved && ac.currentAction) return new Set(ac.currentAction.allowedInstruments || []);
  const step = state.currentStep;
  if (!step) return new Set();
  const meta = stepMeta(keyOf(step));
  return new Set(step.allowedInstruments?.length ? step.allowedInstruments : (meta?.allowed || []));
}

function buildTray() {
  const exp = expectedCodes();
  const q = trayFilter.text.trim().toLocaleLowerCase("tr");
  const visible = INSTRUMENT_CATALOG.filter((i) => {
    if (trayFilter.expectedOnly && !exp.has(i.code)) return false;
    if (trayFilter.category !== "ALL" && (i.category || "Diğer") !== trayFilter.category) return false;
    if (!q) return true;
    return `${i.name} ${i.code} ${i.setCode || ""}`.toLocaleLowerCase("tr").includes(q);
  });

  els.instrumentGrid.innerHTML = "";
  if (visible.length === 0) {
    els.instrumentGrid.innerHTML = `<p class="tray-empty muted">Eşleşen malzeme yok.</p>`;
    return;
  }

  const groups = new Map();
  for (const ins of visible) {
    const cat = ins.category || "Diğer";
    if (!groups.has(cat)) groups.set(cat, []);
    groups.get(cat).push(ins);
  }
  for (const cat of categoriesInOrder()) {
    const items = groups.get(cat);
    if (!items) continue;
    const group = document.createElement("div");
    group.className = "tray-group";
    const head = document.createElement("div");
    head.className = "tray-cat";
    head.textContent = `${catLabel(cat)} (${items.length})`;
    const cards = document.createElement("div");
    cards.className = "tray-group-cards";
    for (const ins of items) cards.appendChild(trayCard(ins));
    group.append(head, cards);
    els.instrumentGrid.appendChild(group);
  }
  highlightExpected(exp);
}

function trayCard(ins) {
  const card = document.createElement("div");
  card.className = "tray-card" + (ins.sterile === false ? " nonsterile" : "")
    + (state.holding === ins.code ? " held" : "");
  card.dataset.code = ins.code;
  card.draggable = true;
  // 60 adımlık modelin kataloğunda set kodu yok; "sette yok (EK)" rozeti yalnızca eski katalogda anlamlı.
  const setInfo = ins.setCode ? ` · ${ins.setCode} (×${ins.quantity})` : (isFlowModel() ? "" : " · sette yok (sarf/ek)");
  card.title = `${ins.name}${setInfo}${ins.sterile === false ? " · STERİL DEĞİL" : ""}`;
  card.innerHTML =
    `<span class="tray-ic">${ins.icon}</span>` +
    `<span class="tray-nm">${escapeHtml(ins.name)}</span>` +
    (ins.setCode || !isFlowModel() ? `<span class="tray-code">${ins.setCode ? escapeHtml(ins.setCode) : "EK"}</span>` : "") +
    (ins.sterile === false ? `<span class="tray-flag">steril değil</span>` : "");
  card.addEventListener("click", () => pickUp(ins.code));
  card.addEventListener("dragstart", (e) => { pickUp(ins.code); e.dataTransfer.setData("text/plain", ins.code); });
  return card;
}

function highlightExpected(exp) {
  els.instrumentGrid.querySelectorAll(".tray-card").forEach((c) =>
    c.classList.toggle("expected", exp.has(c.dataset.code)));
  scrollExpectedIntoView();
}

// Beklenen malzemeyi yatay rayda görünür kılar; kullanıcı elle aramak zorunda kalmaz.
function scrollExpectedIntoView() {
  const first = els.instrumentGrid.querySelector(".tray-card.expected");
  if (!first) return;
  const rail = els.instrumentGrid;
  const left = first.offsetLeft - rail.clientWidth / 2 + first.clientWidth / 2;
  rail.scrollTo({ left: Math.max(0, left), behavior: "smooth" });
}

// ---- Akış şeması: motorun bu seansta yürüdüğü yol (60 adımlık model) ----
// Yeşil = yürünen, mavi = şu anki adım, gri = atlanan dal, kesikli ok = karar dalı (koşul adıyla).
let flowRenderNo = 0;

async function openFlow() {
  if (!isFlowModel()) { toast("bad", "Akış şeması yalnızca 60 adımlık modelde var."); return; }
  els.flowModal.classList.remove("hidden");
  await refreshFlow();
}

async function refreshFlow() {
  let flow = null;
  if (state.sessionId) {
    try { flow = await getJson(`/api/sessions/${state.sessionId}/flow`); } catch (e) { log("error", "✕ Akış okunamadı", formatError(e)); }
  }
  renderFlowPlan(flow);
  await renderFlowDiagram(flow);
}

const shortLabel = (t, n = 34) => (t.length > n ? t.slice(0, n - 1) + "…" : t).replaceAll('"', "'");
const nid = (id) => `n${String(id).replace(/[^A-Za-z0-9]/g, "")}`;

function buildMermaid(flow) {
  const path = flow?.path || [];
  const current = flow?.currentNodeId ?? null;
  const visited = new Set(current ? path.slice(0, -1) : path);
  const order = FLOW_GRAPH.map((n) => n.id);
  const reach = current ? order.indexOf(current) : (path.length ? order.length : -1);
  const cls = (id) => id === current ? "current" : visited.has(id) ? "done" : order.indexOf(id) < reach ? "skipped" : "pending";

  const L = ["flowchart TB"];
  // Ana akış yukarıdan aşağıya tek zincir; dallar yana açılır. Alt akışlar (43a, 47a–f) kendi kutusunda.
  // Aşama değişen ilk düğümde aşama adı etikete eklenir.
  let lastPhase = null;
  const label = (n) => {
    const ph = n.phaseId ? Number(n.phaseId.replace("PH", "")) : null;
    const head = ph && ph !== lastPhase ? `[${ph}. ${shortLabel(FLOW_PHASES[ph] || "", 30)}] ` : "";
    if (ph) lastPhase = ph;
    return `${head}${n.id}. ${shortLabel(n.label, 44)}`;
  };
  for (const n of FLOW_GRAPH.filter((x) => !x.subflowId)) L.push(`${nid(n.id)}["${label(n)}"]:::${cls(n.id)}`);
  for (const sf of [...new Set(FLOW_GRAPH.filter((x) => x.subflowId).map((x) => x.subflowId))]) {
    L.push(`subgraph SF_${sf}["⑂ ${sf === "COUNT_DISCREPANCY" ? "Sayım uyuşmazlığı alt akışı" : "Yıkama sonrası kanama"}"]`);
    for (const n of FLOW_GRAPH.filter((x) => x.subflowId === sf)) L.push(`${nid(n.id)}["${n.id}. ${shortLabel(n.label, 44)}"]:::${cls(n.id)}`);
    L.push("end");
  }
  const edges = [];
  for (const n of FLOW_GRAPH) {
    for (const b of n.branches) edges.push({ from: n.id, to: String(b.goTo), label: b.when + (b.maxIterations ? ` ≤${b.maxIterations}` : ""), branch: true });
    if (n.next != null) edges.push({ from: n.id, to: String(n.next), branch: false });
  }
  edges.forEach((e) => L.push(e.branch ? `${nid(e.from)} -.->|${e.label}| ${nid(e.to)}` : `${nid(e.from)} --> ${nid(e.to)}`));

  // Yürünen kenarlar: yoldaki ardışık her çift (dal alındıysa dal kenarı, değilse düz kenar).
  const walked = new Set();
  for (let i = 1; i < path.length; i++) {
    const a = path[i - 1], b = path[i];
    const idx = edges.findIndex((e) => e.from === a && e.to === b && e.branch) >= 0 && FLOW_GRAPH.find((n) => n.id === a)?.next !== b
      ? edges.findIndex((e) => e.from === a && e.to === b && e.branch)
      : edges.findIndex((e) => e.from === a && e.to === b);
    if (idx >= 0) walked.add(idx);
  }
  edges.forEach((e, i) => {
    if (walked.has(i)) L.push(`linkStyle ${i} stroke:#34d399,stroke-width:3px`);
    else if (e.branch) L.push(`linkStyle ${i} stroke:#f59e0b,stroke-width:1.5px`);
  });
  L.push("classDef done fill:#14532d,stroke:#34d399,color:#d1fae5",
    "classDef current fill:#1e3a8a,stroke:#93c5fd,stroke-width:3px,color:#ffffff",
    "classDef skipped fill:#1f2937,stroke:#4b5563,color:#6b7280,stroke-dasharray:4 3",
    "classDef pending fill:#172036,stroke:#2a3a63,color:#cbd5e1");
  return L.join("\n");
}

async function renderFlowDiagram(flow) {
  if (!window.mermaid) { els.flowDiagram.innerHTML = `<p class="muted">Mermaid yüklenemedi (internet bağlantısı?).</p>`; return; }
  if (!renderFlowDiagram.ready) {
    mermaid.initialize({ startOnLoad: false, theme: "dark", securityLevel: "strict", maxTextSize: 200000,
      flowchart: { curve: "basis", nodeSpacing: 30, rankSpacing: 26, htmlLabels: false, useMaxWidth: false } });
    renderFlowDiagram.ready = true;
  }
  const keep = { left: els.flowDiagram.scrollLeft, top: els.flowDiagram.scrollTop };
  try {
    const { svg } = await mermaid.render(`flowSvg${++flowRenderNo}`, buildMermaid(flow));
    els.flowDiagram.innerHTML = svg;
    const cur = els.flowDiagram.querySelector("g.node.current");
    if (cur && !renderFlowDiagram.scrolled) {
      cur.scrollIntoView({ block: "center", inline: "center" });
      renderFlowDiagram.scrolled = true;
    } else els.flowDiagram.scrollTo(keep.left, keep.top);
  } catch (e) {
    els.flowDiagram.innerHTML = `<pre class="muted">${escapeHtml(String(e?.message || e))}</pre>`;
  }
  const path = flow?.path || [];
  els.flowSummary.textContent = flow
    ? `${flow.scenarioId} · ${path.length} adım yüründü · şu an: ${flow.currentNodeId ?? "bitti"}`
    : "Seans yok — yalnızca model gösteriliyor";
}

function renderFlowPlan(flow) {
  if (!flow) { els.flowPlan.innerHTML = `<p class="muted">Seansı başlatınca koşul planı burada görünür.</p>`; els.flowRequests.innerHTML = ""; return; }
  const val = (v) => v ? "evet" : "hayır";
  els.flowPlan.innerHTML = flow.conditions.map((c) => {
    const plan = c.plan ? (c.plan.length > 1 ? c.plan.map(val).join(" → ") : val(c.plan[0])) : "hayır (plan yok)";
    const yes = c.plan?.some(Boolean);
    return `<div class="fp-row"><span class="fp-code">${escapeHtml(c.code)}</span>` +
      `<span class="fp-val ${yes ? "yes" : "no"}">${plan}</span>` +
      `<span class="fp-desc">${escapeHtml(c.description)} · adım ${escapeHtml(String(c.evaluatedAt ?? "-"))} · ${escapeHtml(c.source)}</span>` +
      (c.evaluations ? `<span class="fp-eval">değerlendirildi: ${c.evaluations} · dala girildi: ${c.taken}</span>` : "") + `</div>`;
  }).join("") +
    `<div class="fp-row"><span class="fp-code">Hasta</span><span class="fp-val">${escapeHtml(flow.parameters.PATIENT_BODY_TYPE ?? "-")}</span>` +
    `<span class="fp-desc">kapatma: ${escapeHtml(flow.parameters.SKIN_CLOSURE_TYPE ?? "-")} · mezo damar: ${(flow.parameters.MESO_VESSELS || []).map((v) => v.caliber).join(", ") || "-"}` +
    ` · kompres kayıt/masa: ${flow.counts.recorded.COMPRESS ?? "-"}/${flow.counts.table.COMPRESS ?? "-"}</span></div>`;
  const req = Object.entries(flow.requests || {});
  els.flowRequests.innerHTML = req.length
    ? req.map(([n, names]) => `<div class="fp-row"><span class="fp-code">Adım ${escapeHtml(n)}</span><span class="fp-val">${escapeHtml(names.join(" / "))}</span></div>`).join("")
    : `<p class="muted">Bu vakada cerrah istemi yok.</p>`;
}

// ---- Hata bildirimi ----
// Bildirim sunucuya (backend/AppData/issues.json) yazılır; ngrok üzerinden bağlanan
// kullanıcının raporu local diskte kalıcı olur.
function openBugModal() {
  els.bugResult.classList.add("hidden");
  els.bugResult.textContent = "";
  els.bugSendBtn.disabled = false;
  els.bugModal.classList.remove("hidden");
  els.bugBody.focus();
}

function bugContext() {
  const step = state.currentStep;
  const meta = step ? stepMeta(keyOf(step)) : null;
  const ac = state.activeComplication;
  return {
    url: location.href,
    backend: baseUrl(),
    sessionId: state.sessionId || "-",
    scenario: els.scenario.value,
    phase: step ? `${step.phaseNo ?? meta?.phaseNo ?? "-"} · ${step.phase ?? meta?.phase ?? "-"}` : "-",
    step: step ? `${keyOf(step)}/${TOTAL_STEPS} · ${step.title ?? ""}` : "-",
    model: PROCEDURE_MODEL,
    expected: step ? (step.allowedInstruments || meta?.allowed || []).join(" / ") : "-",
    holding: state.holding || "-",
    activeComplication: ac && !ac.resolved ? `${ac.title} (${ac.resolutionIndex + 1}/${ac.resolutionTotal})` : "-",
    score: els.scoreValue.textContent,
    counters: `doğru=${state.counters.correct} hatalı=${state.counters.wrong} steril=${state.counters.sterile} komp=${state.counters.complication}`,
    viewport: `${window.innerWidth}x${window.innerHeight}`,
    userAgent: navigator.userAgent,
  };
}

async function sendBugReport() {
  const body = els.bugBody.value.trim();
  if (!body) { els.bugBody.focus(); toast("bad", "Lütfen ne olduğunu yazın."); return; }
  els.bugSendBtn.disabled = true;
  try {
    const issue = await postJson("/api/issues", {
      title: els.bugTitle.value.trim(),
      body,
      severity: els.bugSeverity.value,
      area: els.bugArea.value || null,
      reportedBy: els.bugReporter.value.trim() || auth.user?.username,
      context: bugContext(),
    });
    els.bugResult.className = "bug-result ok";
    els.bugResult.textContent = `✔ Kaydedildi — #${issue.number} (${issue.id}). Teşekkürler!`;
    els.bugTitle.value = "";
    els.bugBody.value = "";
    toast("good", `Hata bildirimi alındı — #${issue.number}`);
    log("success", `🐞 Hata bildirildi #${issue.number}`, `${issue.title}\n${issue.body}`);
    setTimeout(() => els.bugModal.classList.add("hidden"), 1600);
  } catch (e) {
    els.bugResult.className = "bug-result err";
    els.bugResult.textContent = "✕ Gönderilemedi. Backend adresini kontrol edip tekrar deneyin.";
    log("error", "✕ Hata bildirimi gönderilemedi", formatError(e));
  } finally {
    els.bugSendBtn.disabled = false;
  }
}

function bindTrayTools() {
  if (!els.traySearch) return;
  els.traySearch.addEventListener("input", () => {
    trayFilter.text = els.traySearch.value;
    buildTray();
  });
  els.trayExpectedBtn.addEventListener("click", () => {
    trayFilter.expectedOnly = !trayFilter.expectedOnly;
    if (trayFilter.expectedOnly) { trayFilter.category = "ALL"; trayFilter.text = ""; els.traySearch.value = ""; }
    els.trayExpectedBtn.classList.toggle("on", trayFilter.expectedOnly);
    buildTrayChips();
    buildTray();
  });
  const nudge = (dir) => els.instrumentGrid.scrollBy({ left: dir * Math.round(els.instrumentGrid.clientWidth * 0.8), behavior: "smooth" });
  els.trayPrev.addEventListener("click", () => nudge(-1));
  els.trayNext.addEventListener("click", () => nudge(1));
  // Dikey tekerlek hareketini yatay kaydırmaya çevirir.
  els.instrumentGrid.addEventListener("wheel", (e) => {
    if (Math.abs(e.deltaY) <= Math.abs(e.deltaX)) return;
    e.preventDefault();
    els.instrumentGrid.scrollLeft += e.deltaY;
  }, { passive: false });
  // Sürükleme sırasında rayın kenarına gelince otomatik kaydır.
  els.instrumentGrid.addEventListener("dragover", (e) => {
    const r = els.instrumentGrid.getBoundingClientRect();
    if (e.clientX < r.left + 50) els.instrumentGrid.scrollLeft -= 18;
    else if (e.clientX > r.right - 50) els.instrumentGrid.scrollLeft += 18;
  });
}

function buildSutures() {
  els.sutureSelect.innerHTML = `<option value="">(iplik yok)</option>`;
  for (const s of SUTURE_CATALOG) {
    const opt = document.createElement("option");
    opt.value = s.code; opt.textContent = s.name;
    els.sutureSelect.appendChild(opt);
  }
}

function bindZones() {
  document.querySelectorAll(".zone").forEach((z) => {
    z.addEventListener("click", () => deliverToZone(z.dataset.zone));
    z.addEventListener("dragover", (e) => { e.preventDefault(); z.classList.add("drag-over"); });
    z.addEventListener("dragleave", () => z.classList.remove("drag-over"));
    z.addEventListener("drop", (e) => { e.preventDefault(); z.classList.remove("drag-over"); deliverToZone(z.dataset.zone); });
  });
}

function bindEvents() {
  els.startBtn.addEventListener("click", startSession);
  els.endBtn.addEventListener("click", endSession);
  els.autoBtn.addEventListener("click", autoDeliver);
  els.releaseBtn.addEventListener("click", release);
  els.countSendBtn.addEventListener("click", sendCount);
  els.confirmBtn.addEventListener("click", () => sendEvent({ eventType: "step_confirmed" }));
  els.checklistSendBtn.addEventListener("click", sendChecklist);
  els.clearLogBtn.addEventListener("click", () => (els.log.innerHTML = ""));
  els.trafficClear.addEventListener("click", () => { els.traffic.innerHTML = ""; trafficCount = 0; els.trafficCount.textContent = "0 istek"; });
  els.audioToggle.addEventListener("click", toggleAudio);
  els.trafficToggle.addEventListener("click", () => {
    const expanded = !els.traffic.classList.contains("expanded");
    els.traffic.classList.toggle("expanded", expanded);
    els.traffic.classList.toggle("compact", !expanded);
    els.trafficToggle.textContent = expanded ? "Daralt" : "Genişlet";
  });
  els.logToggle.addEventListener("click", () => {
    els.log.classList.toggle("hidden");
    els.logToggle.textContent = els.log.classList.contains("hidden")
      ? "▸ Geliştirici Günlüğü (JSON)" : "▾ Geliştirici Günlüğü (JSON)";
  });
  els.helpBtn.addEventListener("click", () => els.helpModal.classList.remove("hidden"));
  els.closeHelpBtn.addEventListener("click", () => els.helpModal.classList.add("hidden"));
  els.bugBtn.addEventListener("click", openBugModal);
  els.closeBugBtn.addEventListener("click", () => els.bugModal.classList.add("hidden"));
  els.bugSendBtn.addEventListener("click", sendBugReport);
  // Ctrl/Cmd+Enter ile gönder, Esc ile kapat.
  els.bugBody.addEventListener("keydown", (e) => {
    if ((e.metaKey || e.ctrlKey) && e.key === "Enter") sendBugReport();
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") { els.bugModal.classList.add("hidden"); els.flowModal.classList.add("hidden"); }
  });
  els.closeReportBtn.addEventListener("click", () => els.reportModal.classList.add("hidden"));
  els.flowBtn.addEventListener("click", openFlow);
  els.loginForm.addEventListener("submit", login);
  els.logoutBtn.addEventListener("click", async () => { if (state.sessionId) await endSession(); setAuth(null, null); showLogin(); });
  els.closeFlowBtn.addEventListener("click", () => { els.flowModal.classList.add("hidden"); renderFlowDiagram.scrolled = false; });
  document.querySelectorAll("button[data-evt]").forEach((btn) =>
    btn.addEventListener("click", () => sendEvent({ eventType: btn.dataset.evt })));
}

// ---- Başlangıç ----
if (location.hostname !== "localhost" && location.hostname !== "127.0.0.1") {
  els.baseUrl.value = location.origin;
}
bindZones();
bindEvents();
setConn(false);
resetComplications();
baselineVitals();
startMonitor();

// Adım listesi ve malzeme kataloğu backend'den yüklenir.
(async function boot() {
  try {
    const p = await loadProcedure();
    await restoreAuth();
    buildTrayChips();
    buildTray();
    bindTrayTools();
    buildSutures();
    buildStepList();
    els.progressLabel.textContent = `Aşama 0 / ${PHASE_COUNT}`;
    log("info", "⚙ Prosedür yüklendi",
      `${p.title}\nModel: ${PROCEDURE_MODEL} · Aşama: ${PHASE_COUNT} · Adım: ${TOTAL_STEPS} · Alet: ${INSTRUMENT_CATALOG.length}` +
      (isFlowModel() ? ` · Karar koşulu: ${(p.conditions || []).length}` : ` · Komplikasyon: ${p.complications.length}`));
  } catch (e) {
    log("error", "✕ Prosedür yüklenemedi", formatError(e));
    toast("bad", "Prosedür yüklenemedi — backend adresini kontrol edin.");
  }
})();
