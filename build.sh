#!/usr/bin/env bash
# Builds two plugin archives from the same source:
#   Memberra-Jellyfin-<v>-10.11.zip  (net9.0,  Jellyfin 10.11.x, targetAbi 10.11.0.0)
#   Memberra-Jellyfin-<v>-12.zip     (net10.0, Jellyfin 12.x,    targetAbi 12.0.0.0)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
version=$(sed -n 's/.*Version = "\([^"]*\)".*/\1/p' "$here/MemberraProtocol.cs")
engine=$(command -v podman || command -v docker)
user_args=()
[[ "$engine" == *docker ]] && user_args=(-u "$(id -u):$(id -g)" -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp)
rm -rf "$here/artifacts"
for pair in "net9.0:10.11:10.0" "net10.0:12:10.0"; do
  IFS=: read -r tfm label sdk <<<"$pair"
  out="$here/artifacts/$label/Memberra"
  mkdir -p "$out"
  "$engine" run --rm "${user_args[@]}" -v "$here:/src:Z" -w /src "mcr.microsoft.com/dotnet/sdk:$sdk" \
    dotnet publish Memberra.Jellyfin.csproj -c Release -f "$tfm" -o "/src/artifacts/$label/Memberra"
  find "$out" -type f ! -name 'Memberra.Jellyfin.dll' -delete
  zip_name="Memberra-Jellyfin-${version}-${label}.zip"
  if command -v zip >/dev/null; then
    (cd "$here/artifacts/$label" && zip -X -FS "../$zip_name" Memberra/Memberra.Jellyfin.dll)
  else
    (cd "$here/artifacts/$label" && python3 -m zipfile -c "../$zip_name" Memberra)
  fi
  (cd "$here/artifacts" && sha256sum "$zip_name" > "$zip_name.sha256" && md5sum "$zip_name" > "$zip_name.md5")
done
ls -la "$here/artifacts"/*.zip
