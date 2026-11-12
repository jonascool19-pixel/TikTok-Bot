#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

if (( EUID != 0 )); then printf '%s\n' 'Bitte als root im Ubuntu-CT starten.' >&2; exit 1; fi
source /etc/os-release
if [[ "$ID:$VERSION_ID:$(uname -m)" != ubuntu:24.04:x86_64 ]]; then
  printf '%s\n' 'Dieses Paket ist für Ubuntu 24.04 auf x86-64 vorgesehen.' >&2
  exit 1
fi

skip_upgrade=false
if [[ $# == 1 && "$1" == --skip-os-upgrade ]]; then skip_upgrade=true
elif [[ $# != 0 ]]; then printf '%s\n' 'Unbekannte Option.' >&2; exit 1; fi

base=/opt/tiktok-bot
state=/var/lib/tiktok-bot
unit=/etc/systemd/system/tiktok-bot.service
port=
log=/var/log/tiktok-bot-install.log
touch "$log"
chmod 0600 "$log"
exec 9>/run/lock/tiktok-bot-install.lock
flock -n 9 || { printf '%s\n' 'Eine Installation läuft bereits.' >&2; exit 1; }
exec 3>&1
exec > >(tee -a "$log") 2>&1
export DEBIAN_FRONTEND=noninteractive NEEDRESTART_MODE=l

tmp=$(mktemp -d)
trap 'rm -rf -- "$tmp"' EXIT
trap 'printf "\nInstallation fehlgeschlagen (Zeile %s). Log: %s\n" "$LINENO" "$log" >&2' ERR
info() { printf '\n\033[1;36m%s\033[0m\n' "$*"; }

info 'Ubuntu-Paketlisten aktualisieren'
apt-get update
if [[ "$skip_upgrade" == false ]]; then
  info 'Ubuntu-Pakete aktualisieren'
  apt-get -y -o Dpkg::Options::=--force-confold upgrade
fi
apt-get install -y --no-install-recommends ca-certificates curl espeak-ng iproute2 python3-venv xz-utils

node_version=24.22.3
node_sha=4f2b175a6d53a5c617a80dba2676238a0688cef28c6da4eba35c7d844e66fcfb
runtime="$base/runtime-$node_version"
install -d -m 0755 "$base" "$base/versions"
if [[ ! -x "$runtime/bin/node" ]]; then
  info "Node.js $node_version einrichten"
  curl --fail --silent --show-error --location --retry 3 \
    "https://nodejs.org/dist/v$node_version/node-v$node_version-linux-x64.tar.xz" -o "$tmp/node.tar.xz"
  printf '%s  %s\n' "$node_sha" "$tmp/node.tar.xz" | sha256sum -c -
  mkdir "$tmp/runtime"
  tar -xJf "$tmp/node.tar.xz" -C "$tmp/runtime" --strip-components=1
  chmod -R a+rX "$tmp/runtime"
  mv -T "$tmp/runtime" "$runtime"
fi
export PATH="$runtime/bin:$PATH"
if [[ -r "$unit" ]]; then
  port=$(sed -nE 's/^Environment=BOT_PORT=([0-9]{1,5})$/\1/p' "$unit" | tail -n 1)
fi
if [[ -r /etc/tiktok-bot.env ]]; then
  configured_port=$(sed -nE 's/^BOT_PORT=([0-9]{1,5})$/\1/p' /etc/tiktok-bot.env | tail -n 1)
  port="${configured_port:-$port}"
fi
if [[ -z "$port" ]]; then
  port=$(node -e 'const s = require("node:net").createServer(); s.listen(0, "0.0.0.0", () => { console.log(s.address().port); s.close(); });')
fi

info 'Natürliche Stimmen einrichten'
piper="$base/piper-1.8.0"
models="$base/voices"
if [[ ! -f "$piper/.ready" ]]; then
  python3 -m venv "$piper"
  "$piper/bin/pip" install --disable-pip-version-check --no-cache-dir 'piper-tts==1.8.0'
  touch "$piper/.ready"
fi
install -d -m 0755 "$models"
for voice in de/de_DE/thorsten/medium/de_DE-thorsten-medium de/de_DE/kerstin/low/de_DE-kerstin-low en/en_US/amy/low/en_US-amy-low de/de_DE/thorsten_emotional/medium/de_DE-thorsten_emotional-medium; do
  for suffix in onnx onnx.json; do
    file="${voice##*/}.$suffix"
    if [[ ! -s "$models/$file" ]]; then
      curl -fsSL --retry 3 "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/$voice.$suffix" -o "$tmp/$file"
      install -m 0644 "$tmp/$file" "$models/$file"
    fi
  done
done
chmod -R a+rX "$piper"

info 'TikTok-Bot installieren'
here=$(cd -- "$(dirname -- "$(realpath -- "$0")")" && pwd)
if [[ -f "$here/package-lock.json" && -f "$here/src/server.js" ]]; then
  source_dir=$here
else
  ref=v0.1.22
  curl --fail --silent --show-error --location --retry 3 \
    "https://codeload.github.com/jonascool19-pixel/TikTok-Bot/tar.gz/refs/tags/$ref" -o "$tmp/source.tgz"
  mkdir "$tmp/source"
  tar -xzf "$tmp/source.tgz" -C "$tmp/source" --strip-components=1
  source_dir="$tmp/source"
fi
mkdir "$tmp/app"
cp "$source_dir/package.json" "$source_dir/package-lock.json" "$tmp/app/"
cp -R "$source_dir/src" "$source_dir/web" "$tmp/app/"
(cd "$tmp/app" && npm ci --omit=dev --ignore-scripts --no-audit --no-fund)
PLAYWRIGHT_BROWSERS_PATH="$base/playwright" node "$tmp/app/node_modules/playwright/cli.js" install --with-deps --only-shell chromium
chmod -R a+rX "$base/playwright"
chmod -R a+rX "$tmp/app"
target="$base/versions/$(date -u +%Y%m%d%H%M%S)-$$"
mv -T "$tmp/app" "$target"

if ! id tiktok-bot >/dev/null 2>&1; then
  useradd --system --user-group --home-dir "$state" --shell /usr/sbin/nologin tiktok-bot
fi
install -d -m 0700 -o tiktok-bot -g tiktok-bot "$state"
pending="$state/.installer-password"
if [[ ! -e "$state/state.json" ]]; then
  runuser -u tiktok-bot -- env BOT_DATA="$state" "$runtime/bin/node" "$target/src/server.js" --init >"$pending"
fi

old=$(readlink "$base/current" || true)
ln -s "$target" "$base/current.new"
mv -Tf "$base/current.new" "$base/current"
cat >"$unit" <<UNIT
[Unit]
Description=TikTok-Bot
Wants=network-online.target
After=network-online.target

[Service]
User=tiktok-bot
Group=tiktok-bot
WorkingDirectory=$base/current
Environment=BOT_DATA=$state
Environment=BOT_HOST=0.0.0.0
Environment=BOT_PORT=$port
Environment=BOT_PIPER=$piper/bin/piper
Environment=BOT_PIPER_MODELS=$models
Environment=PLAYWRIGHT_BROWSERS_PATH=$base/playwright
EnvironmentFile=-/etc/tiktok-bot.env
ExecStart=$runtime/bin/node $base/current/src/server.js
Restart=on-failure
RestartSec=4
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=$state
UMask=0077

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload

ip=$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{ for(i=1;i<NF;i++) if($i=="src") { print $(i+1); exit } }' || true)
if [[ -z "$ip" ]]; then ip=$(ip -4 -o addr show scope global | awk 'NR==1 {split($4,a,"/"); print a[1]}'); fi
dashboard_ip=
healthy=false
if systemctl restart tiktok-bot; then
  for _ in {1..25}; do
    if systemctl is-active --quiet tiktok-bot; then
      for candidate in "${ip:-127.0.0.1}" 127.0.0.1; do
        if curl --fail --silent --max-time 2 "http://$candidate:$port/health" | grep -Fxq '{"ready":true,"service":"tiktok-bot"}'; then
          dashboard_ip=$candidate
          healthy=true
          break 2
        fi
      done
    fi
    sleep 1
  done
fi
if [[ "$healthy" != true ]]; then
  journalctl -u tiktok-bot -n 25 --no-pager || true
  if [[ -n "$old" ]]; then
    ln -s "$old" "$base/current.new"
    mv -Tf "$base/current.new" "$base/current"
    systemctl restart tiktok-bot || true
  else
    systemctl stop tiktok-bot || true
  fi
  printf '\nDashboard nicht erreichbar. Installationslog: %s\n' "$log" >&2
  exit 1
fi
systemctl enable tiktok-bot

password='Bestehendes Passwort verwenden'
if [[ -s "$pending" ]]; then
  password=$(cat "$pending")
  rm -f -- "$pending"
fi
printf '\n\033[1;32m┌────────────────────────────────────────────────────┐\n' >&3
printf '│ %-50s │\n' 'TikTok-Bot ist erreichbar' "Webseite: http://$dashboard_ip:$port" 'Benutzer: admin' "Passwort: $password" >&3
printf '└────────────────────────────────────────────────────┘\033[0m\n' >&3
