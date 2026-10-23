// Arqenion — dikey akış hattı (60 adımlık model).
// Ana hat solda dikey bir çizgi (1 → 60); koşullu dallar (12, 20–21, 41) bir sağ şeritte,
// komplikasyon alt akışları (43a, 47a–f) daha sağda, alt akışın kendi dalları en sağda.
// Geriye dönüşler (29 → 28, 43a → 42, 47c → 47) solda ince bir kenar yolundan çizilir.
// Adım adları sağdaki sütunda; durum motorun yürüdüğü yoldan (/flow) ve olay kayıtlarından (/events) gelir.

const FlowRail = (() => {
  const LX = [30, 54, 78, 102];          // şerit x'leri: ana hat, dal, alt akış, alt akış dalı
  const LABEL_X = 122;
  const R = 10, ROW = 27, ROW_CUR = 46, PHASE_H = 28, GUTTER = 8;

  const COND = {
    EXPOSURE_INSUFFICIENT: "Görüş yetersiz",
    ADHESIONS_PRESENT: "Yapışıklık",
    LIGATURE_NOT_SECURE: "Bağ güvenli değil",
    BLEEDING_DETECTED: "Kanama",
    BLEEDING_AFTER_IRRIGATION: "Yıkama sonrası kanama",
    COUNT_MISMATCH: "Sayım uyuşmazlığı",
    ITEM_FOUND_ON_RECOUNT: "Tekrar sayımda bulundu",
    ITEM_FOUND_IN_FIELD: "Sahada bulundu",
    ITEM_FOUND_ON_IMAGING: "Görüntülemede bulundu",
  };
  const LANE_NAME = ["Ana hat", "Dal", "Alt akış", "Alt akış dalı"];

  let M = null;            // model: düğümler, aşama adları, koşul açıklamaları
  let host = null;         // kaydırma kutusu
  let L = null;            // yerleşim: sıra, şerit, aşama
  let lastCurrent;         // takip: şu anki düğüm değişince ortaya kaydırılır
  let userScrollAt = 0;

  const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const condName = (code) => COND[code] || M?.conditions[code] || code || "";

  function init(el, graph, phaseNames, conditions) {
    host = el;
    const nodes = (graph || []).map((n) => ({
      id: String(n.id), kind: n.kind, label: n.label, phaseId: n.phaseId, subflowId: n.subflowId,
      next: n.next == null ? null : String(n.next),
      branches: (n.branches || []).map((b) => ({ when: b.when, goTo: String(b.goTo), max: b.maxIterations })),
    }));
    M = { nodes, byId: new Map(nodes.map((n) => [n.id, n])), phaseNames: phaseNames || {}, conditions: conditions || {} };
    L = null; lastCurrent = undefined;
    // Kullanıcı kendisi kaydırırsa birkaç saniye otomatik takibe ara verilir.
    for (const ev of ["wheel", "touchmove", "mousedown"]) host.addEventListener(ev, () => (userScrollAt = Date.now()), { passive: true });
  }

  // Sıra: ana hat ve koşullu adımlar model sırasıyla; alt akış girdiği adımın hemen ardına.
  // Şerit: koşullu adım 1; alt akış 2; alt akış içindeki dal 3.
  function buildLayout(showPreop) {
    const lane = new Map(), phase = new Map(), order = [];
    const subEntry = new Map();
    for (const n of M.nodes) if (!n.subflowId)
      for (const b of n.branches) { const t = M.byId.get(b.goTo); if (t?.subflowId) subEntry.set(n.id, t); }

    for (const n of M.nodes) {
      if (n.subflowId || (n.kind === "preop" && !showPreop)) continue;
      lane.set(n.id, n.kind === "conditional" ? 1 : 0);
      phase.set(n.id, n.phaseId || (n.kind === "preop" ? "PRE" : null));
      order.push(n.id);
      const first = subEntry.get(n.id);
      if (!first) continue;
      const queue = [[first.id, 2]];
      while (queue.length) {
        const [id, l] = queue.shift();
        if (lane.has(id)) continue;
        lane.set(id, l);
        const sn = M.byId.get(id);
        if (sn.next && M.byId.get(sn.next)?.subflowId === first.subflowId) queue.push([sn.next, l]);
        for (const b of sn.branches) if (M.byId.get(b.goTo)?.subflowId === first.subflowId) queue.push([b.goTo, Math.min(l + 1, 3)]);
      }
      for (const s of M.nodes.filter((x) => x.subflowId === first.subflowId)) {
        order.push(s.id); phase.set(s.id, n.phaseId);
        if (!lane.has(s.id)) lane.set(s.id, 2);
      }
    }
    return { lane, phase, order, row: new Map(order.map((id, i) => [id, i])), showPreop };
  }

  // ---- Seansın durumu ----
  function analyse(flow, events) {
    const path = (flow?.path || []).map(String);
    const current = flow ? (flow.currentNodeId == null ? null : String(flow.currentNodeId)) : undefined;
    const completed = !!flow && current === null;
    const visits = new Map();
    for (const id of current != null ? path.slice(0, -1) : path) visits.set(id, (visits.get(id) || 0) + 1);
    const walked = new Set();
    for (let i = 1; i < path.length; i++) walked.add(`${path[i - 1]}>${path[i]}`);
    const latest = path.length > 1 ? `${path[path.length - 2]}>${path[path.length - 1]}` : null;

    const errors = new Map(); let ok = 0, err = 0;
    for (const e of events || []) {
      if (!e.nodeId && e.stepId == null) continue;
      if (e.isSuccess === false) { err++; const k = String(e.nodeId ?? e.stepId); errors.set(k, (errors.get(k) || 0) + 1); }
      else if (e.isSuccess === true) ok++;
    }

    const reach = !flow ? -1 : completed ? Infinity : (L.row.get(current) ?? -1);
    const status = (id) => {
      if (id === current) return "cur";
      if (visits.get(id)) return "ok";
      return (L.row.get(id) ?? Infinity) < reach ? "skip" : "pend";
    };

    let maxMain = 0;
    for (const id of visits.keys()) if (/^\d+$/.test(id)) maxMain = Math.max(maxMain, Number(id));
    if (completed) maxMain = 60;

    return { path, current, completed, visits, walked, latest, errors, ok, err, status, maxMain,
      excursions: excursions(path, current, errors, flow) };
  }

  // Dal günlüğü: ana hattan dala/alt akışa her iniş ve ana hatta dönüş noktası.
  function excursions(path, current, errors, flow) {
    const lane = (id) => L.lane.get(id) ?? 0;
    const out = []; let open = null;
    for (let i = 1; i < path.length; i++) {
      const a = path[i - 1], b = path[i];
      const via = M.byId.get(a)?.branches.find((x) => x.goTo === b);
      const la = lane(a), lb = lane(b);
      if (la === 0 && lb > 0) { open = { from: a, when: via?.when, nodes: [b], deepest: lb, returnTo: null }; out.push(open); }
      else if (la > 0 && lb > 0 && open) { open.nodes.push(b); open.deepest = Math.max(open.deepest, lb); }
      else if (la > 0 && lb === 0 && open) { open.returnTo = b; open = null; }
      else if (la === 0 && lb === 0 && via && M.byId.get(a).next !== b)
        out.push({ from: a, when: via.when, nodes: [b], deepest: 0, returnTo: b, loop: true });
    }
    for (const x of out) {
      x.errors = x.nodes.filter((id) => id !== current).reduce((s, id) => s + (errors.get(id) || 0), 0);
      x.open = !x.returnTo;
      x.cause = (flow?.adaptiveDecisions || []).find((d) => d.condition === x.when && !d.outcome);
    }
    return out;
  }

  // ---- Geometri ----
  function geometry(current) {
    const pos = new Map(), phases = [];
    let y = 6, lastPhase = null;
    for (const id of L.order) {
      const ph = L.phase.get(id);
      if (ph && ph !== lastPhase) { phases.push({ phase: ph, y }); y += PHASE_H; lastPhase = ph; }
      const h = id === current ? ROW_CUR : ROW;
      const lane = L.lane.get(id);
      pos.set(id, { x: LX[lane], y: y + h / 2, top: y, h, lane });
      y += h;
    }
    return { pos, phases, height: y + 12 };
  }

  function edgesOf(pos) {
    const out = [];
    const add = (from, to, branch, when, max) => {
      const a = pos.get(from), b = pos.get(to);
      const back = L.row.get(to) <= L.row.get(from);
      let kind;
      if (back) kind = a.lane === 0 && b.lane === 0 ? "loop" : "ret";
      else if (a.lane === 0 && b.lane === 0) kind = "track";
      else if (b.lane === 0) kind = "ret";
      else kind = `l${b.lane}`;
      out.push({ from, to, branch, when, max, back, kind });
    };
    for (const id of L.order) {
      const n = M.byId.get(id);
      if (n.next && pos.has(n.next)) add(id, n.next, false);
      for (const b of n.branches) if (pos.has(b.goTo)) add(id, b.goTo, true, b.when, b.max);
    }
    return out;
  }

  function edgePath(e, pos) {
    const a = pos.get(e.from), b = pos.get(e.to);
    if (e.back) {
      // Sol kenar yolu: düğümden sola, yukarı, hedefin soluna.
      const g = GUTTER, r = 6;
      return `M${a.x - R},${a.y} H${g + r} Q${g},${a.y} ${g},${a.y - r} V${b.y + r} Q${g},${b.y} ${g + r},${b.y} H${b.x - R - 3}`;
    }
    if (a.x === b.x) return `M${a.x},${a.y + R} V${b.y - R - (e.kind === "track" ? 0 : 3)}`;
    const y0 = a.y + R * 0.7, y1 = b.y - R - 3;
    if (b.x > a.x) {
      // Daha derine iniş: hemen yana kıvrılır, sonra hedefe kadar düz iner.
      const k = Math.min(y1, y0 + 20);
      return `M${a.x + R * 0.5},${y0} C${a.x + 4},${k} ${b.x},${y0 + 4} ${b.x},${k}` + (y1 > k ? ` V${y1}` : "");
    }
    // Ana hatta (ya da daha sığ şeride) dönüş: kendi şeridinde iner, son satırda hedefe kıvrılır.
    const k = Math.max(y0, y1 - 20);
    return `M${a.x},${a.y + R}` + (k > a.y + R ? ` V${k}` : "") + ` C${a.x},${y1 - 4} ${b.x + 4},${k} ${b.x + R * 0.4},${y1 + 1}`;
  }

  // ---- Çizim ----
  function render(flow, events) {
    if (!M || !host) return null;
    const showPreop = !!flow?.path?.some((id) => String(id).startsWith("PRE"));
    if (!L || L.showPreop !== showPreop) L = buildLayout(showPreop);
    const A = analyse(flow, events);
    const { pos, phases, height } = geometry(A.current);
    const W = LABEL_X;

    const svg = [`<svg class="fr-svg" width="${W}" height="${height}" viewBox="0 0 ${W} ${height}" xmlns="http://www.w3.org/2000/svg"><defs>` +
      ["ok", "l1", "l2", "l3", "pend"].map((k) => `<marker id="fr-ar-${k}" viewBox="0 0 10 10" refX="7" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" class="fr-ar-${k}"/></marker>`).join("") +
      `</defs>`];

    // Şu anki satırın vurgusu (etiket sütunuyla birlikte HTML'de devam eder).
    if (A.current && pos.has(A.current)) {
      const p = pos.get(A.current);
      svg.push(`<rect class="fr-curband" x="0" y="${p.top + 2}" width="${W}" height="${p.h - 4}"/>`);
    }

    const edges = edgesOf(pos).map((e) => {
      const key = `${e.from}>${e.to}`;
      const on = A.walked.has(key);
      let cls, marker = "";
      if (e.kind === "track") {
        const bypass = !on && A.status(e.from) === "ok" && ["ok", "cur"].includes(A.status(e.to));
        cls = `fr-e fr-track${on ? " on" : bypass ? " bypass" : ""}`;
      } else {
        cls = `fr-e ${on ? "fr-on" : "fr-opt"} ${e.kind}`;
        const mk = e.kind === "ret" || e.kind === "loop" ? (on ? "ok" : "pend") : e.kind;
        marker = ` marker-end="url(#fr-ar-${mk})"`;
      }
      if (on && key === A.latest && !A.completed) cls += " latest";
      return { on, d: `<path class="${cls}" d="${edgePath(e, pos)}"${marker}/>` };
    });
    edges.filter((x) => !x.on).forEach((x) => svg.push(x.d));
    edges.filter((x) => x.on).forEach((x) => svg.push(x.d));

    for (const [id, p] of pos) {
      const s = A.status(id);
      const errs = A.errors.get(id) || 0;
      const label = id.startsWith("PRE") ? "Ö" + id.slice(4) : id;
      svg.push(`<g class="fr-node st-${s} lane${p.lane}" transform="translate(${p.x},${p.y})">` +
        (s === "cur" ? `<circle class="fr-halo" r="${R}"/>` : "") +
        `<circle class="fr-dot" r="${R}"/><text class="fr-num${label.length > 2 ? " sm" : ""}" y="0.5">${esc(label)}</text>` +
        (errs ? `<g class="fr-badge" transform="translate(${R - 1},${-R + 1})"><circle r="6.5"/><text>${errs}</text></g>` : "") + `</g>`);
    }
    svg.push("</svg>");

    // Etiket sütunu: aşama başlıkları ve adım adları.
    const curPhase = A.current ? L.phase.get(A.current) : null;
    const html = [];
    for (const ph of phases) {
      const no = ph.phase === "PRE" ? null : Number(ph.phase.replace("PH", ""));
      const name = ph.phase === "PRE" ? "Ameliyat öncesi" : M.phaseNames[no] || ph.phase;
      const mains = L.order.filter((id) => L.phase.get(id) === ph.phase && L.lane.get(id) === 0);
      const done = mains.length && mains.every((id) => A.status(id) === "ok");
      const active = ph.phase === curPhase;
      const cls = active ? "active" : done ? "done" : "";
      html.push(`<div class="fr-phase ${cls}" style="top:${ph.y}px;height:${PHASE_H}px">` +
        `<span class="fr-ph-no">${no ?? "Ö"}</span><span class="fr-ph-nm">${esc(name)}</span>${done && !active ? `<span class="fr-ph-ok">✓</span>` : ""}</div>`);
    }

    const entryOf = new Map(), returnTo = new Map();
    for (const x of A.excursions) {
      if (!x.loop) entryOf.set(x.nodes[0], x);
      else entryOf.set(x.from, x);
      if (x.returnTo && !x.loop) returnTo.set(x.returnTo, x);
    }
    for (const [id, p] of pos) {
      const n = M.byId.get(id);
      const s = A.status(id);
      const x = entryOf.get(id);
      const chips = [];
      if (x) {
        const tip = x.cause ? `Nedeni: ${x.cause.atNode}. adımdaki hata (${x.cause.deviationType})` : condName(x.when);
        chips.push(`<span class="fr-chip ${x.loop ? "loop" : `l${Math.max(1, x.deepest)}`}" title="${esc(tip)}">${x.loop ? "↺" : x.deepest >= 2 ? "⤓" : "↘"} ${esc(condName(x.when))}</span>`);
      }
      if (returnTo.has(id) && s !== "pend") chips.push(`<span class="fr-chip ret" title="Dal tamamlandı, ana hatta dönüldü">↩ ana hat</span>`);
      const v = A.visits.get(id) || 0;
      if (v > 1) chips.push(`<span class="fr-chip rep">×${v}</span>`);
      const meta = s === "cur" ? `<span class="fr-now">Şu an · ${LANE_NAME[p.lane]}</span>` : "";
      html.push(`<div class="fr-row st-${s} lane${p.lane}" style="top:${p.top}px;height:${p.h}px" title="${esc(id)}. ${esc(n.label)}">` +
        `<div class="fr-txt">${meta}<span class="fr-title">${esc(n.label)}</span></div>${chips.join("")}</div>`);
    }

    host.innerHTML = `<div class="fr-canvas" style="height:${height}px">${svg.join("")}<div class="fr-labels">${html.join("")}</div></div>`;

    if (A.current !== lastCurrent) {
      const first = lastCurrent === undefined;
      lastCurrent = A.current;
      if (Date.now() - userScrollAt > 5000) {
        const target = A.current && pos.get(A.current);
        const top = target ? target.y - host.clientHeight * 0.38 : A.completed ? height : 0;
        host.scrollTo({ top: Math.max(0, top), behavior: first ? "auto" : "smooth" });
      }
    }
    last = { A, L, M };
    return A;
  }

  // Genişletilmiş görünümler (flow-views.js) son çizimin verisini kullanır.
  let last = null;
  const state = () => last;

  return { init, render, condName, state };
})();
