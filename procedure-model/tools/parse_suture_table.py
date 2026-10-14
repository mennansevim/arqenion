# -*- coding: utf-8 -*-
"""Cerrahi sütürlerin tablosu (.docx) → suture_reference.json"""
import sys, json, re, docx
d = docx.Document(sys.argv[1])
t = d.tables[0]
rows = [[c.text.strip() for c in r.cells] for r in t.rows]
groups = [(0, 1, True, "MONOFILAMENT"), (2, 3, True, "MULTIFILAMENT"), (4, 5, False, "MONOFILAMENT"), (6, 7, False, "MULTIFILAMENT")]
out = []
for r in rows[3:]:
    for ni, di, ab, struct in groups:
        cell = r[ni] if ni < len(r) else ""
        if not cell:
            continue
        lines = [x.strip() for x in cell.split("\n") if x.strip()]
        name, material = lines[0], " ".join(lines[1:])
        days = r[di].strip() if di < len(r) else ""
        foot = re.findall(r"[¹²³*]", name + days)
        clean_name = re.sub(r"[¹²³]", "", name).strip()
        dd = days.replace("*", "").strip()
        rng = None
        if ab and dd and dd != "-":
            nums = [int(x) for x in re.findall(r"\d+", dd)]
            rng = {"min": nums[0], "max": nums[-1], "approx": dd.startswith("≈"), "text": days}
        e = {"name": clean_name, "material": material, "absorbable": ab, "structure": struct, "absorptionDays": rng}
        if foot:
            e["footnoteMarks"] = foot
        if e not in out:
            out.append(e)
json.dump({"source": "Cerrahi_süturaların_tablosu.docx (Fatih Candemir, 2026-10-07)",
           "note": "Tablodaki ¹ ² ³ * dipnot işaretlerinin açıklaması belgede yok.",
           "sutures": out}, open(sys.argv[2], "w"), ensure_ascii=False, indent=1)
from collections import Counter
print(len(out), Counter((e["absorbable"], e["structure"]) for e in out))
for e in out[:3] + [x for x in out if x["name"].startswith(("Vicryl", "Monocryl", "Prolene", "İpek", "Ethilon"))]:
    print(e)
