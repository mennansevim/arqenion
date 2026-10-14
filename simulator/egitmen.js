// Eğitmen ekranı: kullanıcılar, seanslar, hata bildirimleri. Simülatörle aynı girişi (localStorage jetonu) kullanır.
const $ = (id) => document.getElementById(id);
const TOKEN_KEY = "arqenion.token";
let token = null;
try { token = localStorage.getItem(TOKEN_KEY); } catch { /* gizli pencere */ }

const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const fmtDate = (s) => (s ? new Date(s).toLocaleString("tr-TR", { dateStyle: "short", timeStyle: "short" }) : "—");
const SCENARIOS = { "case-basic-01": "Standart", "case-adhesion-01": "Yapışıklık", "case-bleeding-01": "Kanama",
  "case-count-01": "Sayım uyuşmazlığı", "case-obese-01": "Kilolu hasta", "case-full-01": "Tam vaka" };

async function api(method, path, body) {
  const res = await fetch(path, {
    method, headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (res.status === 401) { setToken(null); showLogin("Oturum süreniz doldu; yeniden giriş yapın."); throw new Error("401"); }
  const text = await res.text();
  const data = text ? JSON.parse(text) : {};
  if (!res.ok) { const e = new Error(data.message || `HTTP ${res.status}`); e.status = res.status; throw e; }
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
  let me;
  try { me = await api("GET", "/api/auth/me"); } catch { return; }
  if (me.role !== "instructor") { showLogin("Bu ekran eğitmenler içindir. Eğitmen hesabıyla girin."); return; }
  $("who").textContent = `${me.name || me.username} · eğitmen`;
  $("loginForm").classList.add("hidden");
  $("app").classList.remove("hidden");
  $("logoutBtn").classList.remove("hidden");
  await Promise.all([loadUsers(), loadSessions(), loadIssues()]);
}

$("loginForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const res = await fetch("/api/auth/login", {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: $("loginUser").value.trim(), password: $("loginPass").value }),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) { showLogin(res.status === 429 ? "Çok fazla deneme. Bir dakika sonra tekrar deneyin." : data.message); return; }
  setToken(data.token);
  $("loginPass").value = "";
  await enter();
});
$("logoutBtn").addEventListener("click", () => { setToken(null); showLogin(); });

// ---- Kullanıcılar ----
// Okunabilir 16 karakterlik şifre (karışabilen 0/O, 1/l yok); tarayıcıda üretilir, sunucuya yalnız özetlenmek üzere gider.
function newPassword() {
  const alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
  return [...crypto.getRandomValues(new Uint8Array(16))].map((b) => alphabet[b % alphabet.length]).join("");
}

function showSecret(username, password) {
  const box = $("secretBox");
  box.innerHTML = `<span><b>${esc(username)}</b> şifresi:</span><code>${esc(password)}</code>` +
    `<button class="btn btn-mini btn-ghost" type="button" id="copySecret">Kopyala</button>` +
    `<span class="muted">Kişiye güvenli bir kanaldan iletin; bu ekran kapanınca tekrar gösterilmez.</span>`;
  box.classList.remove("hidden");
  $("copySecret").addEventListener("click", async (e) => {
    try { await navigator.clipboard.writeText(password); e.target.textContent = "Kopyalandı"; }
    catch { getSelection().selectAllChildren(box.querySelector("code")); }
  });
}

function userError(message) {
  $("userError").textContent = message || "";
  $("userError").classList.toggle("hidden", !message);
}

async function loadUsers() {
  const users = await api("GET", "/api/auth/users");
  $("userCount").textContent = `${users.length}`;
  $("userRows").innerHTML = users.map((u) => `
    <tr>
      <td>${esc(u.username)}</td><td>${esc(u.name)}</td>
      <td>${u.role === "instructor" ? "Eğitmen" : "Kursiyer"}</td>
      <td><span class="pill ${u.isActive ? "ok" : "off"}">${u.isActive ? "aktif" : "pasif"}</span></td>
      <td>${fmtDate(u.createdAt)}</td>
      <td>
        <button class="btn btn-mini btn-ghost" data-reset="${esc(u.id)}" data-name="${esc(u.username)}" type="button">Şifre sıfırla</button>
        <button class="btn btn-mini btn-ghost" data-active="${esc(u.id)}" data-value="${!u.isActive}" type="button">${u.isActive ? "Pasif yap" : "Aktif yap"}</button>
      </td>
    </tr>`).join("");
}

$("userRows").addEventListener("click", async (e) => {
  const b = e.target.closest("button");
  if (!b) return;
  userError("");
  try {
    if (b.dataset.reset) {
      const password = newPassword();
      await api("POST", `/api/auth/users/${b.dataset.reset}/password`, { password });
      showSecret(b.dataset.name, password);
    } else if (b.dataset.active) {
      await api("POST", `/api/auth/users/${b.dataset.active}/active?value=${b.dataset.value}`);
      await loadUsers();
    }
  } catch (err) { userError(err.message); }
});

$("addForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  userError("");
  const password = newPassword();
  try {
    const u = await api("POST", "/api/auth/users", {
      username: $("newUsername").value.trim().toLowerCase(), name: $("newName").value.trim() || null, role: $("newRole").value, password,
    });
    showSecret(u.username, password);
    $("addForm").reset();
    await loadUsers();
  } catch (err) { userError(err.message); }
});

// ---- Seanslar ----
let sessions = [];
async function loadSessions() {
  sessions = await api("GET", "/api/sessions");
  $("sessionCount").textContent = `${sessions.length}`;
  $("sessionRows").innerHTML = sessions.map((s) => `
    <tr class="clickable" data-id="${esc(s.id)}">
      <td>${fmtDate(s.startedAt)}</td><td>${esc(s.username)}</td><td>${esc(SCENARIOS[s.scenarioId] || s.scenarioId)}</td>
      <td>${esc(s.procedureModel)}</td>
      <td><span class="pill ${s.status === "COMPLETED" ? "ok" : "warn"}">${s.status === "COMPLETED" ? "bitti" : "sürüyor"}</span></td>
      <td class="num">${s.score}</td><td class="num">${s.correctActions}</td><td class="num">${s.wrongActions}</td>
      <td class="num">${s.sterileViolations}</td><td class="num">${s.complicationsResolved}/${s.complicationsTriggered}</td>
      <td>${esc(s.currentNodeId ?? (s.status === "COMPLETED" ? "—" : s.currentStepId))}</td>
    </tr>`).join("") || `<tr><td colspan="11" class="muted">Henüz seans yok.</td></tr>`;
}
$("refreshSessions").addEventListener("click", loadSessions);

$("sessionRows").addEventListener("click", async (e) => {
  const row = e.target.closest("tr[data-id]");
  if (!row) return;
  document.querySelectorAll("#sessionRows tr.selected").forEach((r) => r.classList.remove("selected"));
  row.classList.add("selected");
  const s = sessions.find((x) => x.id === row.dataset.id);
  const box = $("sessionDetail");
  box.classList.remove("hidden");
  box.innerHTML = `<p class="muted">Yükleniyor…</p>`;
  const [events, flow] = await Promise.all([
    api("GET", `/api/sessions/${s.id}/events`),
    s.procedureModel === "v1" ? Promise.resolve(null) : api("GET", `/api/sessions/${s.id}/flow`).catch(() => null),
  ]);
  const errors = events.filter((ev) => ev.deviationType);
  box.innerHTML =
    `<b>${esc(s.username)} · ${esc(SCENARIOS[s.scenarioId] || s.scenarioId)} · puan ${s.score}</b>` +
    (flow ? `<div class="path">Yol: ${esc(flow.path.join(" → "))}</div>` : "") +
    (flow?.adaptiveDecisions?.length ? `<div>Hatanın sonucu: ${flow.adaptiveDecisions.map((d) => `${esc(d.condition)} (adım ${esc(d.atNode)})`).join(", ")}</div>` : "") +
    `<div>${errors.length ? `Hatalar (${errors.length}):` : "Hata yok."}</div>` +
    errors.map((ev) => `<div class="muted">Adım ${esc(ev.nodeId ?? ev.stepId)} · ${esc(ev.deviationType)} · ${esc(ev.instrumentCode || ev.eventType)} · ${ev.scoreChange}</div>`).join("");
});

// ---- Hata bildirimleri ----
async function loadIssues() {
  const issues = await api("GET", "/api/issues?status=open");
  const list = issues.issues || [];
  $("issueCount").textContent = `${list.length}`;
  $("issues").innerHTML = list.map((i) => `
    <div class="issue">
      <b>#${i.number} ${esc(i.title)}</b>
      <div class="meta">${esc(i.severity)} · ${esc(i.area || "—")} · ${esc(i.reportedBy)} · ${fmtDate(i.createdAt)}</div>
      <div>${esc(i.body)}</div>
      <form class="row-form" data-resolve="${esc(i.id)}">
        <label class="field" style="flex:1"><span>Çözüm notu</span><input type="text" required placeholder="Ne yapıldı?" /></label>
        <button class="btn btn-mini btn-primary" type="submit">Kapat</button>
      </form>
    </div>`).join("") || `<p class="muted">Açık hata bildirimi yok.</p>`;
}

$("issues").addEventListener("submit", async (e) => {
  e.preventDefault();
  const form = e.target.closest("form[data-resolve]");
  await api("POST", `/api/issues/${form.dataset.resolve}/resolve`, { resolution: form.querySelector("input").value.trim() });
  await loadIssues();
});

if (token) enter(); else showLogin();
