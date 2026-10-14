#!/usr/bin/env bash
# Açık apendektomi modelini kaynaklardan yeniden üretir: model → doğrulama → RAG parçaları + graf → Mermaid → golden.
# Gereken: python3 (3.10+), pdftotext (poppler). Kullanım: ./procedure-model/build.sh [sürüm]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; ROOT="$(cd "$HERE/.." && pwd)"
VER="${1:-2.4}"
OUT="$HERE/dist/open-appendectomy-v$VER.json"
python3 "$HERE/tools/build.py" "$ROOT/arqenion.excalidraw" "$ROOT/docs/Apendektomi ameliyatı.docx.pdf" "$OUT" >/dev/null
python3 "$HERE/tools/validate.py" "$OUT"
python3 "$HERE/tools/rag_export.py" "$OUT"
python3 "$HERE/tools/flow_engine.py" --graph "${OUT%.json}.graph.json" --full > "${OUT%.json}.full.mmd"
python3 "$HERE/tools/make_golden.py" "$VER"   # backend golden testlerinin beklediği sonuçlar
echo "tamam: $OUT"
