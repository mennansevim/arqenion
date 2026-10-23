#!/bin/sh
# Simülatör arayüzünü Vercel'e (arqenion-sim projesi) yayınlar. Backend'e config.js'teki adresten (Pi, ngrok) gider.
#   ./deploy/vercel/deploy.sh          → canlı (production)
#   ./deploy/vercel/deploy.sh preview  → önizleme
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SITE="$ROOT/deploy/vercel/site"          # git dışı; .vercel bağlantısı burada kalır
mkdir -p "$SITE"
rsync -a --delete --exclude .vercel --exclude tok-test.txt "$ROOT/simulator/" "$SITE/"
cd "$SITE"
[ -d .vercel ] || npx -y vercel@latest link --yes --project arqenion-sim
if [ "$1" = "preview" ]; then npx -y vercel@latest deploy --yes; else npx -y vercel@latest deploy --prod --yes; fi
