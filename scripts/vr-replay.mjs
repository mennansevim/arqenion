// VR oturumu kaydı ve tekrar oynatma (docs/vr-events.md).
//
//   node scripts/vr-replay.mjs record <plan.json> <kayit.json> [--url http://localhost:5080]
//   node scripts/vr-replay.mjs replay <kayit.json>... [--url http://localhost:5080]
//   Giriş: --user/--pass, ARQ_USER/ARQ_PASS ya da yerelde backend/.env'deki ilk eğitmen hesabı.
//
// record: planı canlı sunucuda oynatır. Her adımda doğru olayı adım görünümünden üretir; plan belirli düğümlerde
//         araya hata, sözleşme ihlali ya da aynı olayın tekrarını karıştırır. Gönderilen her JSON olay ve alınan
//         cevabın özeti kayda yazılır.
// replay: kayıttaki JSON olayları VR gibi birebir gönderir ve her cevabı kayıtla karşılaştırır.
//         Aynı kayıtlar `dotnet test backend.Tests` içinde süreç içi HTTP ile de oynatılır (VrReplayTests).

import { readFile, writeFile } from "node:fs/promises";
import { login } from "./auth.mjs";

const argv = process.argv.slice(2);
const flag = (name, def) => { const i = argv.indexOf(`--${name}`); return i >= 0 ? argv[i + 1] : def; };
const positional = argv.filter((a, i) => !a.startsWith("--") && !argv[i - 1]?.startsWith("--"));
const [mode, ...files] = positional;
const URL_ = (flag("url", "http://localhost:5080")).replace(/\/$/, "");
const STAMP = "2026-10-14T09:00:00.000Z";
let TOKEN = null;

async function post(path, body) {
  const res = await fetch(`${URL_}${path}`, {
    method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${TOKEN}` }, body: JSON.stringify(body ?? {}),
  });
  const text = await res.text();
  return { status: res.status, body: text ? JSON.parse(text) : {} };
}

// Cevabın karşılaştırılan özeti: karar, düğüm, puan, dal, hataya göre dal, bitiş.
function summarize(status, b) {
  if (status !== 200) return { status, field: b.field ?? null };
  return {
    status, success: b.success, currentNodeId: b.currentNodeId ?? null, nextNodeId: b.nextNodeId ?? null,
    deviationType: b.deviation?.deviationType ?? null, score: b.score,
    branch: b.branch ? `${b.branch.condition}${b.branch.resolved ? ":resolved" : ""}` : null,
    adaptive: (b.adaptive ?? []).map((a) => a.condition).sort(), completed: !!b.completed,
  };
}

function correctEvent(step) {
  switch (step.expectedEvent) {
    case "step_confirmed": return { eventType: "step_confirmed" };
    case "count_confirmed": return { eventType: "count_confirmed", counts: { [step.countCategory]: step.tableCount } };
    case "checklist_confirmed": return { eventType: "checklist_confirmed", checkedItems: (step.checklist ?? []).map((i) => i.id) };
  }
  const instrument = step.allowedInstruments?.[0] ?? step.requiredSuture;
  const ev = { eventType: "instrument_delivered", instrumentCode: instrument, target: "SURGEON" };
  if (step.requiredSuture && step.requiredSuture !== instrument) ev.sutureCode = step.requiredSuture;
  return ev;
}

async function record(planPath, outPath) {
  const plan = JSON.parse(await readFile(planPath, "utf8"));
  const start = await post("/api/sessions", plan.start);
  if (start.status !== 200) throw new Error(`seans başlamadı: ${JSON.stringify(start.body)}`);
  const sid = start.body.sessionId;
  const pending = new Map();
  for (const inj of plan.inject ?? []) pending.set(inj.at, [...(pending.get(inj.at) ?? []), inj]);

  const events = [];
  let step = start.body.currentStep, n = 0, last = null;
  const send = async (ev) => {
    const r = await post(`/api/sessions/${sid}/events`, ev);
    events.push({ send: ev, expect: summarize(r.status, r.body) });
    if (r.status === 200) { last = ev; step = r.body.completed ? null : r.body.nextStep; }
    return r;
  };
  const fresh = (fields) => ({ eventId: `${plan.name}-${String(++n).padStart(3, "0")}`, timestamp: STAMP, ...fields });

  for (let guard = 0; step && guard < 400; guard++) {
    for (const inj of pending.get(step.nodeId) ?? []) {
      if (inj.duplicateLast) await send({ ...last });       // aynı eventId: ikinci kez işlenmemeli
      else await send(fresh(inj.send));
    }
    pending.delete(step.nodeId);
    if (step) await send(fresh(correctEvent(step)));
  }
  if (step) throw new Error(`akış bitmedi (düğüm ${step.nodeId})`);

  const report = (await post(`/api/sessions/${sid}/complete`)).body;
  const out = {
    name: plan.name, description: plan.description, start: plan.start, events,
    report: { score: report.score, wrongActions: report.wrongActions, correctActions: report.correctActions,
              complicationsTriggered: report.complicationsTriggered, complicationsResolved: report.complicationsResolved },
  };
  await writeFile(outPath, JSON.stringify(out, null, 1) + "\n");
  console.log(`kaydedildi: ${outPath} · ${events.length} olay · skor ${report.score} · dal ${report.complicationsTriggered}/${report.complicationsResolved}`);
}

async function replay(path) {
  const rec = JSON.parse(await readFile(path, "utf8"));
  const start = await post("/api/sessions", rec.start);
  if (start.status !== 200) return [`${rec.name}: seans başlamadı`];
  const sid = start.body.sessionId, problems = [];
  for (const [i, e] of rec.events.entries()) {
    const r = await post(`/api/sessions/${sid}/events`, e.send);
    const got = summarize(r.status, r.body);
    if (JSON.stringify(got) !== JSON.stringify(e.expect))
      problems.push(`${rec.name} #${i + 1} ${e.send.eventType} ${e.send.instrumentCode ?? ""}\n    beklenen ${JSON.stringify(e.expect)}\n    gelen    ${JSON.stringify(got)}`);
  }
  const report = (await post(`/api/sessions/${sid}/complete`)).body;
  for (const [k, v] of Object.entries(rec.report))
    if (report[k] !== v) problems.push(`${rec.name} rapor.${k}: beklenen ${v}, gelen ${report[k]}`);
  console.log(`${problems.length ? "✕" : "✓"} ${rec.name} · ${rec.events.length} olay`);
  return problems;
}

if (mode === "record" || mode === "replay") TOKEN = await login(URL_, argv);
if (mode === "record" && files.length === 2) await record(files[0], files[1]);
else if (mode === "replay" && files.length) {
  const problems = (await Promise.all(files.map(replay))).flat();
  problems.forEach((p) => console.log("  ", p));
  process.exit(problems.length ? 1 : 0);
} else {
  console.log("kullanım: vr-replay.mjs record <plan.json> <kayit.json> | replay <kayit.json>... [--url …]");
  process.exit(2);
}
