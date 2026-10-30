#!/usr/bin/env bash
set -euo pipefail
BASE=/opt/scum-hub-bot-migration
CONFIG=/etc/scum-hub-bot-migration.env
OVERRIDE=/etc/systemd/system/scum-hub-bot.service.d/90-migration.conf
case "${1:-prepare}" in
prepare)
  if [ ! -d "$BASE/.git" ]; then
    git clone --branch scum-hub-migration --single-branch https://github.com/k1t0q/furyhub-discord-bot.git "$BASE"
  fi
  cd "$BASE"
  git fetch origin scum-hub-migration
  git checkout --detach origin/scum-hub-migration
  npm install --ignore-scripts --omit=dev
  node --check src/server.js
  node --test tests/*.test.js
  echo "Migration code prepared. Running bot unchanged."
  ;;
activate)
  test -s "$CONFIG" || { echo "Migration environment not configured"; exit 1; }
  test "$(stat -c %u "$CONFIG")" = 0
  test "$(stat -c %a "$CONFIG")" = 600
  test -s "$BASE/src/server.js"
  grep -q '^CF_D1_API_TOKEN=.' "$CONFIG"
  grep -q '^IMAGEKIT_PRIVATE_KEY=.' "$CONFIG"
  mkdir -p "$(dirname "$OVERRIDE")"
  test ! -e "$OVERRIDE" || cp "$OVERRIDE" "$OVERRIDE.previous"
  printf '%s\n' '[Service]' "EnvironmentFile=$CONFIG" "WorkingDirectory=$BASE" 'ExecStart=' "/usr/bin/node $BASE/src/server.js" > "$OVERRIDE"
  systemctl daemon-reload
  if ! systemctl restart scum-hub-bot || ! systemctl is-active --quiet scum-hub-bot; then
    rm -f "$OVERRIDE"
    if [ -f "$OVERRIDE.previous" ]; then mv "$OVERRIDE.previous" "$OVERRIDE"; fi
    systemctl daemon-reload
    systemctl restart scum-hub-bot
    echo "Activation failed; previous bot restored"
    exit 1
  fi
  echo "Migration bot active"
  ;;
rollback)
  rm -f "$OVERRIDE"
  systemctl daemon-reload
  systemctl restart scum-hub-bot
  systemctl is-active scum-hub-bot
  ;;
status)
  systemctl is-active scum-hub-bot
  test ! -e "$OVERRIDE" || echo "Migration override exists"
  test ! -s "$CONFIG" || echo "Migration environment exists"
  ;;
*) echo "Allowed: prepare, activate, rollback, status"; exit 2 ;;
esac
