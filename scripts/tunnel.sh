#!/usr/bin/env bash
# Arqenion yayın tüneli — ngrok ajanı sessizce ölebiliyor; bu gözetmen düşerse yeniden başlatır.
# Kullanım:  ./scripts/tunnel.sh          (ön planda, terminali açık bırakın)
#            nohup ./scripts/tunnel.sh &  (arka planda)
set -u

PORT="${PORT:-5080}"
LOG="${LOG:-/tmp/arqenion-tunnel.log}"

echo "[$(date '+%H:%M:%S')] gözetmen başladı — port $PORT, log $LOG" | tee -a "$LOG"

while true; do
  echo "[$(date '+%H:%M:%S')] ngrok başlatılıyor…" >> "$LOG"
  ngrok http "$PORT" --log=stdout >> "$LOG" 2>&1
  code=$?
  echo "[$(date '+%H:%M:%S')] ngrok durdu (çıkış $code) — 3 sn sonra yeniden denenecek" >> "$LOG"
  sleep 3
done
