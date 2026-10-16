#!/usr/bin/env bash
set -Eeuo pipefail
cd /opt/scum-hub-bot
git fetch --prune origin main
CURRENT=$(git rev-parse HEAD)
TARGET=$(git rev-parse origin/main)
if [ "$CURRENT" = "$TARGET" ]; then
  echo "Already deployed: $CURRENT"
  exit 0
fi
git diff --quiet && git diff --cached --quiet || { echo "Local changes exist; aborting"; exit 1; }
git merge --ff-only origin/main
if ! systemctl restart scum-hub-bot; then
  git reset --hard "$CURRENT"
  systemctl restart scum-hub-bot || true
  exit 1
fi
sleep 3
if ! systemctl is-active --quiet scum-hub-bot; then
  git reset --hard "$CURRENT"
  systemctl restart scum-hub-bot || true
  echo "Deployment failed; rolled back"
  exit 1
fi
echo "Deployed $TARGET"
