#!/usr/bin/env bash
# Ships the current build to a small Linux VM and (re)starts it behind the Cloudflare Tunnel.
# The heavy build (.NET SDK + npm, too much for 1 GB RAM) runs here. Only the published app (~40 MB)
# goes over SSH; the VM pulls the .NET runtime base image itself and assembles the final image.
# The repository can stay private.
#
#   bash scripts/deploy-vm.sh azureuser@<vm-ip>               # deploy / update
#   bash scripts/deploy-vm.sh azureuser@<vm-ip> --with-data   # first time: also move this Mac's database
#
# Needs .env.vm (gitignored) with GOMOKU_PUBLIC_HOST, CLOUDFLARE_TUNNEL_TOKEN and GOMOKU_POSTGRES_PASSWORD.
set -euo pipefail
cd "$(dirname "$0")/.."

target="${1:?usage: deploy-vm.sh user@host [--with-data]}"
with_data="${2:-}"
remote_dir=gomoku
compose="docker compose -f compose.yaml -f compose.tunnel.yaml"
local_project="$(basename "$PWD")"

for key in GOMOKU_PUBLIC_HOST CLOUDFLARE_TUNNEL_TOKEN GOMOKU_POSTGRES_PASSWORD; do
  grep -q "^$key=." .env.vm 2>/dev/null || { echo "Missing $key in .env.vm" >&2; exit 1; }
done

echo "==> Building the API image locally"
docker compose build migrate   # migrate and api share gomoku-api:local

echo "==> Copying compose files and settings"
ssh "$target" "mkdir -p $remote_dir"
scp -q compose.yaml compose.tunnel.yaml "$target:$remote_dir/"
scp -q .env.vm "$target:$remote_dir/.env"

work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
echo "==> Packing the published app"
cid="$(docker create gomoku-api:local)"
docker cp "$cid:/app" "$work/app"
docker rm "$cid" >/dev/null
tar -C "$work" -czf "$work/app.tgz" app
echo "==> Uploading the app ($(du -h "$work/app.tgz" | cut -f1)) and assembling the image on the VM"
scp -q "$work/app.tgz" "$target:$remote_dir/app.tgz"
# Mirrors the runtime stage of backend/Gomoku.Api/Dockerfile; keep the two in sync.
ssh "$target" "cd $remote_dir && rm -rf image && mkdir image && tar -C image -xzf app.tgz && rm app.tgz && cat >image/Dockerfile <<'DOCKERFILE'
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY app/ ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT [\"dotnet\", \"Gomoku.Api.dll\"]
DOCKERFILE
docker build -q -t gomoku-api:local image >/dev/null && rm -rf image"

# Only one machine may run the tunnel, or Cloudflare splits players between them.
if docker ps --format '{{.Names}}' | grep -q "^${local_project}-cloudflared-1$"; then
  echo "==> Stopping the tunnel on this machine"
  $compose stop cloudflared
fi

if [[ "$with_data" == --with-data ]]; then
  echo "==> Moving the database and sign-in keys from this machine"
  $compose stop api   # no writes after the dump
  $compose exec -T postgres pg_dump -U gomoku -Fc gomoku >"$work/gomoku.dump"
  docker run --rm -v "${local_project}_gomoku_keys:/keys:ro" alpine tar -C /keys -cf - . >"$work/keys.tar"
  scp -q "$work/gomoku.dump" "$work/keys.tar" "$target:$remote_dir/"
  ssh "$target" "cd $remote_dir && $compose create --no-build && $compose up -d --no-build --wait postgres \
    && $compose exec -T postgres pg_restore -U gomoku -d gomoku --clean --if-exists --no-owner <gomoku.dump \
    && docker run --rm -i -v ${remote_dir}_gomoku_keys:/keys alpine tar -C /keys -xf - <keys.tar \
    && rm gomoku.dump keys.tar"
fi

echo "==> Starting on the VM"
ssh "$target" "cd $remote_dir && $compose up -d --no-build --remove-orphans && $compose ps --format 'table {{.Service}}\t{{.Status}}'"
ssh "$target" 'docker image prune -f >/dev/null'

host="$(grep '^GOMOKU_PUBLIC_HOST=' .env.vm | cut -d= -f2)"
for _ in $(seq 1 30); do
  if curl --fail --silent --max-time 5 "https://$host/ready" >/dev/null; then echo "==> Live: https://$host"; exit 0; fi
  sleep 4
done
echo "Warning: https://$host/ready did not answer yet; check: ssh $target 'cd $remote_dir && $compose logs --tail 50'" >&2
exit 1
