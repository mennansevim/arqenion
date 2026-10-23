#!/bin/sh
# Backend + AI servisi + Postgres + ngrok'u Raspberry Pi'ye yayınlar (Docker).
# Kodu rsync ile ~/arqenion'a kopyalar (commit gerekmez), sonra imajları yeniden derleyip başlatır.
# Gizli değerler Pi'de ~/arqenion/deploy/pi/.env içinde durur; bu betik ona dokunmaz.
#   ./deploy/pi/deploy.sh
set -e
PI="${PI:-mennano@192.168.1.60}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SSH="ssh -o BatchMode=yes -o StrictHostKeyChecking=accept-new -i $HOME/.ssh/id_ed25519"
cd "$ROOT"
rsync -az --delete -e "$SSH" \
  --exclude bin --exclude obj --exclude .env --exclude .venv --exclude .venv-vm --exclude __pycache__ \
  --exclude node_modules --exclude .vercel --exclude AppData --exclude logs --exclude 'deploy/vercel/site' \
  --include '/backend/***' --include '/ai-gateway/***' --include '/simulator/***' --include '/assets/***' --include '/deploy/***' \
  --include '/procedure-model/' --include '/procedure-model/dist/***' --include '/procedure-model/tools/***' \
  --include '/.dockerignore' --exclude '*' ./ "$PI:~/arqenion/"
$SSH "$PI" 'cd ~/arqenion/deploy/pi && docker compose up -d --build && sleep 8 && curl -s localhost:5080/api/health/ready && echo'
