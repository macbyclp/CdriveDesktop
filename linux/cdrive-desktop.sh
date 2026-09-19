#!/usr/bin/env bash
# Cdrive masaüstü başlatıcısı (Linux). Kısayol, Nautilus betiği ve otomatik başlatma bunu çağırır.
DIR="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
ELECTRON="$DIR/node_modules/electron/dist/electron"
if [ ! -x "$ELECTRON" ]; then
  echo "Electron kurulu değil. Önce: cd \"$DIR\" && npm install" >&2
  exit 1
fi
# CDRIVE_NO_SANDBOX=1 yalnız Chromium sandbox'ı kurulamadığında (bkz. README) elle verilir.
EXTRA=()
[ "${CDRIVE_NO_SANDBOX:-}" = "1" ] && EXTRA+=(--no-sandbox)
exec "$ELECTRON" "${EXTRA[@]}" "$DIR" "$@"
