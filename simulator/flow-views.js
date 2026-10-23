// Arqenion — genişletilmiş akış görünümleri (tam ekran).
//   snake: Yılan akış — ana hat satır satır kıvrılır, her adımın adı altında; dallar adımın altına sapıp döner,
//          alt akışlar kesik çizgili kutuda.
//   tree:  Hiyerarşik ağaç — aşama → adım → karar ("Kanama?") → EVET / HAYIR dalları, ana hatta dönüş noktasıyla.
// Veri: FlowRail.state() (son çizimdeki model, yerleşim ve seans durumu).

const FlowViews = (() => {
  const SUBFLOW = { HEMOSTASIS_RECHECK: "Yıkama sonrası kanama", COUNT_DISCREPANCY: "Sayım uyuşmazlığı" };
  const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

  function context() {
    const st = FlowRail.state();
    if (!st) return null;
    const { A, L, M } = st;
    const cond = FlowRail.condName;
    const phaseNo = (ph) => (ph === "PRE" ? "Ö" : Number(String(ph).replace("PH", "")));
    const phaseName = (ph) => (ph === "PRE" ? "Ameliyat öncesi" : M.phaseNames[phaseNo(ph)] || ph);
    const edges = [];
    for (const id of L.order) {
      const n = M.byId.get(id);
      const add = (to, branch, when, max) => {
        if (!L.row.has(to)) return;
        const la = L.lane.get(id), lb = L.lane.get(to), back = L.row.get(to) <= L.row.get(id);
        const kind = back ? (la === 0 && lb === 0 ? "loop" : "ret") : la === 0 && lb === 0 ? "track" : lb === 0 ? "ret" : `l${lb}`;
        edges.push({ from: id, to, branch, when, max, back, kind, on: A.walked.has(`${id}>${to}`) });
      };
      if (n.next) add(n.next, false);
      for (const b of n.branches) add(b.goTo, true, b.when, b.max);
    }
    const phases = [];
    for (const id of L.order) { const ph = L.phase.get(id); if (ph && !phases.includes(ph)) phases.push(ph); }
    const curPh = A.current ? L.phase.get(A.current) : null;
    const phaseState = (ph) => {
      const mains = L.order.filter((id) => L.phase.get(id) === ph && L.lane.get(id) === 0);
      return ph === curPh ? "act" : mains.length && mains.every((id) => A.status(id) === "ok") ? "done" : "";
    };
    return { A, L, M, cond, phaseNo, phaseName, edges, phases, phaseState };
  }

  function nodeSvg(c, id, x, y, r) {
    const s = c.A.status(id), l = c.L.lane.get(id), e = c.A.errors.get(id) || 0, v = c.A.visits.get(id) || 0;
    const label = id.startsWith("PRE") ? "Ö" + id.slice(4) : id;
    const fs = label.length > 2 ? r * 0.72 : r * 0.9;
    return `<g class="fx-n ${s} l${l}" transform="translate(${x},${y})">` + (s === "cur" ? `<circle class="halo" r="${r + 6}"/>` : "") +
      `<circle class="dot" r="${r}"/><text class="num" style="font-size:${fs}px">${esc(label)}</text>` +
      (e ? `<g class="badge" transform="translate(${r * 0.8},${-r * 0.8})"><circle r="7"/><text>${e}</text></g>` : "") +
      (v > 1 ? `<text class="rep" y="${r + 30}">×${v}</text>` : "") + `</g>`;
  }

  function edgeClass(c, e) {
    if (e.kind === "track") {
      const by = !e.on && c.A.status(e.from) === "ok" && ["ok", "cur"].includes(c.A.status(e.to));
      return `fx-e track ${e.on ? "on" : by ? "by" : ""}`;
    }
    return `fx-e ${e.on ? "on" : "opt"} ${e.kind}`;
  }

  // ---- Yılan akış ----
  function snake(host, c) {
    const W = Math.max(600, host.clientWidth - 8);
    const X0 = 70, R = 13, Y0 = 70, DY = 236;
    const mains = c.L.order.filter((id) => c.L.lane.get(id) === 0);
    const PER = Math.max(6, Math.min(16, Math.floor((W - 2 * X0) / 100) + 1));
    const DX = (W - 2 * X0) / (PER - 1);
    const pos = new Map();
    mains.forEach((id, i) => {
      const r = Math.floor(i / PER), k = i % PER;
      pos.set(id, { x: r % 2 ? X0 + (PER - 1 - k) * DX : X0 + k * DX, y: Y0 + r * DY, r, dir: r % 2 ? -1 : 1 });
    });
    // Dal ve alt akış düğümleri: girdikleri ana adımın altında, akış yönünde.
    const fromOf = (id) => c.L.order.slice(0, c.L.order.indexOf(id)).reverse().find((x) => c.L.lane.get(x) === 0);
    for (const id of c.L.order) {
      if (c.L.lane.get(id) === 0 || pos.has(id)) continue;
      const from = fromOf(id), a = pos.get(from);
      if (!a) continue;
      const sibl = c.L.order.filter((x) => c.L.lane.get(x) > 0 && fromOf(x) === from);
      if (c.M.byId.get(id).subflowId) {
        const l2 = sibl.filter((x) => c.L.lane.get(x) === 2);
        for (const s of sibl) {
          if (pos.has(s)) continue;
          const L = c.L.lane.get(s);
          const k = L === 2 ? l2.indexOf(s) : l2.indexOf(c.L.order[c.L.order.indexOf(s) - 1]) + 0.5;
          pos.set(s, { x: a.x + a.dir * (34 + k * 46), y: a.y + (L === 2 ? 92 : 146), r: a.r, dir: a.dir });
        }
      } else {
        const k = sibl.indexOf(id), m = sibl.length;
        pos.set(id, { x: a.x + a.dir * (DX / 2 + (k - (m - 1) / 2) * 34), y: a.y + 92, r: a.r, dir: a.dir });
      }
    }
    const rows = Math.ceil(mains.length / PER);
    const H = Y0 + (rows - 1) * DY + 190;
    const svg = [`<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg">`];
    // alt akış kutuları
    const subs = new Map();
    for (const n of c.M.nodes) if (n.subflowId && pos.has(n.id)) (subs.get(n.subflowId) || subs.set(n.subflowId, []).get(n.subflowId)).push(pos.get(n.id));
    for (const [sf, ps] of subs) {
      const xs = ps.map((p) => p.x), ys = ps.map((p) => p.y);
      const x1 = Math.min(...xs) - 24, x2 = Math.max(...xs) + 24, y1 = Math.min(...ys) - 26, y2 = Math.max(...ys) + 22;
      svg.push(`<rect class="fx-subbox" x="${x1}" y="${y1}" width="${x2 - x1}" height="${y2 - y1}" rx="12"/>`,
        `<text class="fx-subttl" x="${x1 + 8}" y="${y2 + 15}">ALT AKIŞ · ${esc((SUBFLOW[sf] || sf).toLocaleUpperCase("tr-TR"))}</text>`);
    }
    const path = (e) => {
      const a = pos.get(e.from), b = pos.get(e.to);
      if (e.kind === "track") {
        if (a.r === b.r) return `M${a.x + a.dir * R},${a.y} L${b.x - a.dir * R},${b.y}`;
        const side = a.x + a.dir * 56;
        return `M${a.x + a.dir * R},${a.y} C${side},${a.y} ${side},${b.y} ${b.x + a.dir * R},${b.y}`;
      }
      if (e.kind === "loop") return `M${a.x},${a.y - R} C${a.x},${a.y - 46} ${b.x},${b.y - 46} ${b.x},${b.y - R - 3}`;
      const my = (a.y + b.y) / 2;
      if (b.y > a.y) return `M${a.x},${a.y + R} C${a.x},${my + 10} ${b.x},${my - 10} ${b.x},${b.y - R - 3}`;
      if (b.y < a.y) return `M${a.x},${a.y - R} C${a.x},${my} ${b.x},${my + 10} ${b.x + (b.x > a.x ? -2 : 2)},${b.y + R + 3}`;
      const sg = Math.sign(b.x - a.x) || 1;
      return `M${a.x + sg * R},${a.y} L${b.x - sg * (R + 3)},${b.y}`;
    };
    const es = c.edges.filter((e) => pos.has(e.from) && pos.has(e.to));
    const ds = es.map((e) => ({ on: e.on, s: `<path class="${edgeClass(c, e)}" d="${path(e)}"/>`, e }));
    ds.filter((d) => !d.on).forEach((d) => svg.push(d.s));
    ds.filter((d) => d.on).forEach((d) => svg.push(d.s));
    for (const d of ds) {
      if (!d.e.branch || d.e.kind === "ret") continue;
      const a = pos.get(d.e.from), b = pos.get(d.e.to);
      if (d.e.kind === "loop") { svg.push(`<text class="fx-lbl ok" x="${(a.x + b.x) / 2}" y="${a.y - 56}" text-anchor="middle">↺ ${esc(c.cond(d.e.when))}</text>`); continue; }
      if (c.L.lane.get(d.e.from) !== 0 || c.M.byId.get(d.e.to).subflowId) continue;
      svg.push(`<text class="fx-lbl ${d.e.kind} ${d.on ? "" : "dim"}" x="${b.x + 18}" y="${b.y + 4}">${esc(c.cond(d.e.when))}</text>`);
    }
    for (const [id, p] of pos) svg.push(nodeSvg(c, id, p.x, p.y, c.L.lane.get(id) ? 11 : R));
    svg.push("</svg>");

    const over = [];
    let last = null;
    for (const id of mains) {
      const p = pos.get(id), s = c.A.status(id);
      over.push(`<div class="fx-ttl ${s}" style="left:${p.x}px;top:${p.y + 17}px" title="${esc(id)}. ${esc(c.M.byId.get(id).label)}">${esc(c.M.byId.get(id).label)}</div>`);
      const ph = c.L.phase.get(id);
      if (ph && ph !== last) {
        last = ph;
        const ids = mains.filter((x) => c.L.phase.get(x) === ph);
        const span = Math.max(...ids.map((x) => (pos.get(x).r === p.r ? Math.abs(pos.get(x).x - p.x) : 0))) + DX - 14;
        const place = p.dir > 0 ? `left:${p.x - 10}px` : `left:${p.x + 10}px;transform:translate(-100%,-50%)`;
        over.push(`<div class="fx-ph ${c.phaseState(ph)}" style="${place};top:${p.y - 32}px;max-width:${span}px">${c.phaseNo(ph)} · ${esc(c.phaseName(ph))}</div>`);
      }
    }
    // Ana hatta dönüş etiketleri
    const tagged = new Set();
    for (const x of c.A.excursions) {
      if (!x.returnTo || x.loop || tagged.has(x.returnTo) || !pos.has(x.returnTo)) continue;
      tagged.add(x.returnTo);
      const p = pos.get(x.returnTo);
      over.push(`<div class="fx-ret" style="left:${p.x}px;top:${p.y - 58}px">↩ ana hat</div>`);
    }
    host.innerHTML = `<div class="fx-snake" style="width:${W}px;height:${H}px">${svg.join("")}${over.join("")}</div>`;
    const cur = c.A.current && pos.get(c.A.current);
    if (cur) host.scrollTo({ top: Math.max(0, cur.y - host.clientHeight * 0.4), behavior: "smooth" });
  }

  // ---- Hiyerarşik ağaç ----
  function tree(host, c) {
    const { A, L, M } = c;
    const taken = (a, b) => A.walked.has(`${a}>${b}`);
    const step = (id, d) => {
      const s = A.status(id), e = A.errors.get(id) || 0;
      const label = id.startsWith("PRE") ? "Ö" + id.slice(4) : id;
      return { d, cls: `${s} l${L.lane.get(id)}`, html: `<span class="dt">${esc(label)}${e ? `<i class="eb">${e}</i>` : ""}</span><span class="tx">${s === "cur" ? "ŞU AN · " : ""}${esc(M.byId.get(id).label)}</span>` };
    };
    function sub(id, d, rows, seen) {
      if (seen.has(id)) return; seen.add(id);
      rows.push(step(id, d));
      const n = M.byId.get(id);
      if (n.branches.length) decision(id, d, rows, seen);
      else cont(id, n.next, d, rows, seen);
    }
    function cont(from, to, d, rows, seen) {
      if (!to || !L.lane.has(to)) return;
      const fs = M.byId.get(from).subflowId;
      if (L.lane.get(to) > 0 && (!fs || M.byId.get(to).subflowId === fs)) sub(to, d, rows, seen);
      else rows.push({ d, cls: `leaf ret ${taken(from, to) ? "taken" : ""}`, html: `↩ ${L.lane.get(to) === 0 ? "ana hatta dön" : "devam"}: <b>&nbsp;${esc(to)}</b>${taken(from, to) ? "&nbsp;✓" : ""}` });
    }
    function decision(id, d, rows, seen) {
      const n = M.byId.get(id);
      for (const b of n.branches) {
        const y = taken(id, b.goTo);
        rows.push({ d: d + 1, cls: `dec ${y ? "taken" : ""}`, html: `<span class="ic"></span><span class="q">${esc(c.cond(b.when))}?</span>${b.max ? `<span class="leaf">&nbsp;(en fazla ${b.max} kez)</span>` : ""}` });
        rows.push({ d: d + 2, cls: `out yes ${y ? "taken" : "nottaken"}`, html: `<span class="k">EVET</span><span class="v">&nbsp;→ ${esc(b.goTo)}${L.lane.get(b.goTo) === 0 ? " (tekrar)" : ""}</span>${y ? `<span class="tk">&nbsp;✓ yaşandı</span>` : ""}` });
        if (L.lane.get(b.goTo) > 0) sub(b.goTo, d + 3, rows, seen);
        const nY = n.next && taken(id, n.next);
        rows.push({ d: d + 2, cls: `out no ${nY ? "taken" : "nottaken"}`, html: `<span class="k">HAYIR</span><span class="v">&nbsp;→ ${esc(n.next ?? "—")}</span>${nY ? `<span class="tk">&nbsp;✓</span>` : ""}` });
        if (n.next && L.lane.get(n.next) > 0 && n.subflowId) sub(n.next, d + 3, rows, seen);
      }
    }
    let html = "";
    for (const ph of c.phases) {
      const mains = L.order.filter((id) => L.phase.get(id) === ph && L.lane.get(id) === 0);
      const st = c.phaseState(ph);
      const rows = [];
      for (const id of mains) {
        rows.push(step(id, 0));
        if (M.byId.get(id).branches.length) decision(id, 0, rows, new Set([id]));
      }
      rows.forEach((r, i) => { const nx = rows.slice(i + 1).find((q) => q.d <= r.d); r.last = !nx || nx.d < r.d; });
      html += `<div class="fx-ph-card ${st}"><div class="ph-h"><span class="ph-n">${c.phaseNo(ph)}</span>${esc(c.phaseName(ph))}` +
        `<span class="r">${st === "act" ? "ŞU AN" : st === "done" ? "✓ tamam" : ""}</span></div>` +
        rows.map((r) => `<div class="fx-row ${r.cls} ${r.last ? "last" : ""}" style="--d:${r.d}">${r.html}</div>`).join("") + `</div>`;
    }
    host.innerHTML = `<div class="fx-tree">${html}</div>`;
    const cur = host.querySelector(".fx-ph-card.act");
    if (cur) cur.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "smooth" });
  }

  function render(host, view) {
    const c = context();
    if (!c) { host.innerHTML = `<p class="muted" style="padding:20px">Akış modeli yüklenemedi.</p>`; return; }
    (view === "tree" ? tree : snake)(host, c);
  }

  return { render };
})();
