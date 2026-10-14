// Backend yük testi (60 adımlık model): eşzamanlı seanslar, rastgele hatalar, her cevapta tutarlılık denetimi.
//
//   node scripts/load-test.mjs [--url http://localhost:5080] [--sessions 300] [--concurrency 50] [--noise 0.15] [--seed 1]
//
// Her seans bir senaryoyu baştan sona oynar: adımın StepView'ından doğru olayı üretir, araya rastgele yanlış alet,
// yanlış olay türü, yanlış sayım, ipucu ve steril ihlal karıştırır. Denetlenen değişmezler:
//   - her istek 2xx döner; skor 0–100 arasında kalır
//   - sapma olan olayda düğüm değişmez; doğru olayda düğüm ilerler ya da (klemp/istem) aynı düğümde kalır
//   - seans tamamlanır ve rapor gelir; gelişen komplikasyon = yönetilen komplikasyon
// Çıktı: gecikme p50/p95/p99/maks, saniyedeki istek, hata ve ihlal sayıları.

const args = Object.fromEntries(process.argv.slice(2).reduce((a, x, i, all) => (x.startsWith("--") ? [...a, [x.slice(2), all[i + 1]]] : a), []));
const URL_ = (args.url ?? "http://localhost:5080").replace(/\/$/, "");
const SESSIONS = Number(args.sessions ?? 300);
const CONCURRENCY = Number(args.concurrency ?? 50);
const NOISE = Number(args.noise ?? 0.15);
const SCENARIOS = ["case-basic-01", "case-adhesion-01", "case-bleeding-01", "case-count-01", "case-obese-01"];

let seed = Number(args.seed ?? 1);
const rand = () => ((seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648);
const pick = (xs) => xs[Math.floor(rand() * xs.length)];

const latencies = { start: [], event: [], complete: [] };
const problems = [];
let requests = 0, httpErrors = 0;

async function call(kind, path, body) {
  const t0 = performance.now();
  const res = await fetch(`${URL_}${path}`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body ?? {}),
  });
  const ms = performance.now() - t0;
  requests++;
  latencies[kind].push(ms);
  const text = await res.text();
  if (!res.ok) { httpErrors++; throw new Error(`${res.status} ${path}: ${text.slice(0, 200)}`); }
  return JSON.parse(text);
}

function correctEvent(step) {
  switch (step.expectedEvent) {
    case "step_confirmed": return { eventType: "step_confirmed" };
    case "count_confirmed": return { eventType: "count_confirmed", counts: { [step.countCategory]: step.tableCount } };
  }
  const instrument = step.allowedInstruments?.[0] ?? step.requiredSuture;
  const ev = { eventType: "instrument_delivered", instrumentCode: instrument, target: "SURGEON" };
  if (step.requiredSuture && step.requiredSuture !== instrument) ev.sutureCode = step.requiredSuture;
  return ev;
}

function noiseEvent(step, catalog) {
  const r = rand();
  if (r < 0.45) return { eventType: "instrument_delivered", instrumentCode: pick(catalog), target: "SURGEON" };
  if (r < 0.60) return { eventType: pick(["step_confirmed", "count_confirmed", "instrument_placed"]), counts: { COMPRESS: Math.floor(rand() * 20) } };
  if (r < 0.70) return { eventType: "hint_requested" };
  if (r < 0.80) return { eventType: "sterile_area_violated" };
  if (r < 0.88) return { eventType: "instrument_dropped", instrumentCode: pick(catalog) };
  if (r < 0.94) return { ...correctEvent(step), target: "DIRTY_AREA" };
  return { eventType: "bogus_event" };
}

async function playSession(i, catalog) {
  const scenarioId = SCENARIOS[i % SCENARIOS.length];
  const start = await call("start", "/api/sessions", { scenarioId, userId: `load-${i}` });
  const sid = start.sessionId;
  if (start.procedureModel !== "v2.4") problems.push(`#${i}: model ${start.procedureModel}`);
  let step = start.currentStep, branchesOpened = 0, branchesClosed = 0, events = 0;

  while (step && events < 600) {
    const noisy = rand() < NOISE;
    const ev = { eventId: crypto.randomUUID(), ...(noisy ? noiseEvent(step, catalog) : correctEvent(step)) };
    const node = step.nodeId;
    const r = await call("event", `/api/sessions/${sid}/events`, ev);
    events++;
    if (r.score < 0 || r.score > 100) problems.push(`#${i} ${node}: skor ${r.score}`);
    if (r.deviation && r.nextNodeId !== node) problems.push(`#${i} ${node}: sapmada düğüm değişti → ${r.nextNodeId}`);
    if (!noisy && !r.success) problems.push(`#${i} ${node}: doğru olay reddedildi (${JSON.stringify(ev)}): ${r.message}`);
    if (r.branch) r.branch.resolved ? branchesClosed++ : branchesOpened++;
    if (r.completed) { step = null; break; }
    step = r.nextStep;
  }
  if (step) problems.push(`#${i}: ${events} olayda bitmedi (düğüm ${step.nodeId})`);

  const report = await call("complete", `/api/sessions/${sid}/complete`);
  if (report.complicationsTriggered !== report.complicationsResolved)
    problems.push(`#${i}: komplikasyon ${report.complicationsTriggered} gelişti, ${report.complicationsResolved} yönetildi`);
  if (branchesOpened !== report.complicationsTriggered) problems.push(`#${i}: dal sayısı uyuşmuyor`);
  return events;
}

const pct = (xs, p) => { if (!xs.length) return 0; const s = [...xs].sort((a, b) => a - b); return s[Math.min(s.length - 1, Math.floor(p * s.length))]; };
const fmt = (xs) => `n=${xs.length} p50=${pct(xs, .5).toFixed(1)} p95=${pct(xs, .95).toFixed(1)} p99=${pct(xs, .99).toFixed(1)} maks=${Math.max(0, ...xs).toFixed(1)} ms`;

const proc = await (await fetch(`${URL_}/api/procedure`)).json();
const catalog = [...proc.instrumentCatalog.map((x) => x.code), ...proc.sutureCatalog.map((x) => x.code), "UNKNOWN_CODE"];
console.log(`hedef ${URL_} · model ${proc.procedureModel} · ${SESSIONS} seans · eşzamanlı ${CONCURRENCY} · gürültü %${NOISE * 100}`);

const t0 = performance.now();
let next = 0, totalEvents = 0;
await Promise.all(Array.from({ length: CONCURRENCY }, async () => {
  while (next < SESSIONS) {
    const i = next++;
    try { const n = await playSession(i, catalog); totalEvents += n; }
    catch (e) { problems.push(`#${i}: ${e.message}`); }
  }
}));
const secs = (performance.now() - t0) / 1000;

console.log(`süre ${secs.toFixed(1)} sn · ${requests} istek · ${(requests / secs).toFixed(0)} istek/sn · ${totalEvents} olay`);
console.log(`seans başlat : ${fmt(latencies.start)}`);
console.log(`olay         : ${fmt(latencies.event)}`);
console.log(`rapor        : ${fmt(latencies.complete)}`);
console.log(`HTTP hatası ${httpErrors} · ihlal ${problems.length}`);
for (const p of problems.slice(0, 20)) console.log("  ✕", p);
process.exit(problems.length || httpErrors ? 1 : 0);
