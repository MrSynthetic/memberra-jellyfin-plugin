#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$here/artifacts/Memberra"
podman run --rm -v "$here:/src:Z" -w /src mcr.microsoft.com/dotnet/sdk:9.0 dotnet publish -c Release -o /src/artifacts/Memberra
find "$here/artifacts/Memberra" -type f ! -name 'Memberra.Jellyfin.dll' -delete
(cd "$here/artifacts" && zip -FS Memberra-Jellyfin-1.4.0.zip Memberra/Memberra.Jellyfin.dll)
sha256sum "$here/artifacts/Memberra-Jellyfin-1.4.0.zip" > "$here/artifacts/Memberra-Jellyfin-1.4.0.zip.sha256"
