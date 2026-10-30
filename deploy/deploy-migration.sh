#!/usr/bin/env bash
set -euo pipefail
BASE=/opt/scum-hub-bot-migration
CONFIG=/etc/scum-hub-bot-migration.env
OVERRIDE=/etc/systemd/system/scum-hub-bot.service.d/90-migration.conf
case "${1:-prepare}" in
public-key)
  umask 077
  if [ ! -s /etc/scum-hub-migration-transfer.key ]; then
    openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj /CN=scum-hub-migration -keyout /etc/scum-hub-migration-transfer.key -out /etc/scum-hub-migration-transfer.crt 2>/dev/null
  fi
  cat /etc/scum-hub-migration-transfer.crt
  ;;
configure)
  umask 077
  tmp=$(mktemp)
  trap 'rm -f "$tmp"' EXIT
  base64 -d | openssl cms -decrypt -binary -inform DER -recip /etc/scum-hub-migration-transfer.crt -inkey /etc/scum-hub-migration-transfer.key > "$tmp"
  python3 - "$tmp" "$CONFIG" <<'PY'
import json,sys,subprocess,os
incoming=json.load(open(sys.argv[1]))
allowed={'CF_ACCOUNT_ID','CF_D1_DATABASE_ID','CF_D1_API_TOKEN','IMAGEKIT_PRIVATE_KEY','FURYHUB_URL'}
if set(incoming)!=allowed or not all(isinstance(v,str) and v and '\n' not in v and '\r' not in v for v in incoming.values()):
    raise SystemExit('Invalid migration configuration')
import re,shlex,pathlib
pid=subprocess.check_output(['systemctl','show','scum-hub-bot','--property=MainPID','--value']).decode().strip()
env={}
def keep(k,v):
    if k.startswith(('DISCORD_','SCUM_MAP_','FURY')) or k=='PORT': env[k]=v
def process_env(proc):
    try:
        for entry in open('/proc/'+proc+'/environ','rb').read().split(b'\0'):
            if b'=' in entry: keep(*entry.decode().split('=',1))
        children=pathlib.Path('/proc/'+proc+'/task/'+proc+'/children').read_text().split()
        for child in children: process_env(child)
    except (OSError,UnicodeError): pass
process_env(pid)
unit_env=subprocess.check_output(['systemctl','show','scum-hub-bot','--property=Environment','--value']).decode()
for entry in shlex.split(unit_env):
    if '=' in entry: keep(*entry.split('=',1))
exec_start=subprocess.check_output(['systemctl','show','scum-hub-bot','--property=ExecStart','--value']).decode()
paths=['/opt/scum-hub-bot/.env','/etc/scum-hub-bot.env']
paths+=re.findall(r'--env-file(?:=|\s+)(/[^\s;}"'+"'"+r']+)',exec_start)
paths+=re.findall(r'(?:source|\.)\s+(/[^\s;}"'+"'"+r']+)',exec_start)
for path in paths:
    try:
        if os.stat(path).st_uid!=0: continue
        for line in pathlib.Path(path).read_text().splitlines():
            line=line.strip()
            if not line or line.startswith('#') or '=' not in line: continue
            k,v=line.removeprefix('export ').split('=',1)
            try: parsed=shlex.split(v,comments=True);v=parsed[0] if parsed else ''
            except ValueError: continue
            keep(k.strip(),v)
    except (OSError,UnicodeError): pass
if not env.get('DISCORD_BOT_TOKEN'): raise SystemExit('Existing Discord token unavailable; no changes made')
env.update(incoming)
with open(sys.argv[2]+'.new','w') as output:
    for k,v in env.items(): output.write(k+'='+json.dumps(v,ensure_ascii=True)+'\n')
os.chmod(sys.argv[2]+'.new',0o600)
os.replace(sys.argv[2]+'.new',sys.argv[2])
print('Migration environment configured; running bot unchanged')
PY
  ;;
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
*) echo "Allowed: prepare, public-key, configure, activate, rollback, status"; exit 2 ;;
esac
