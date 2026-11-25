#!/usr/bin/env bash
set -euo pipefail

# Publish the Avalonia Studio shell for one runtime identifier and place the Go
# kernel host beside it, which is where HostLauncher.Locate looks at startup.

usage() {
  cat <<'EOF'
Usage: scripts/build-studio-avalonia.sh [OPTIONS]

Publish the Avalonia Studio shell and the kernel host it launches.

Options:
  --rid RID           Target runtime: win-x64, win-arm64, osx-x64, osx-arm64,
                      linux-x64, linux-arm64. Defaults to the host platform.
  --output DIR        Publish directory. Defaults to
                      desktop/avalonia/bin/publish/<RID>.
  --configuration CFG Build configuration (Release or Debug). Default: Release.
  --no-self-contained Publish a framework-dependent app instead of self-contained.
  --single-file       Produce a single-file executable (implies self-contained).
  --no-restore        Skip dotnet restore.
  -h, --help          Show this help.

The Go host is cross-compiled with CGO disabled and written into the publish
directory as reasonix-studio-host (or reasonix-studio-host.exe on Windows).
EOF
}

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
rid=""
output=""
configuration="Release"
self_contained="true"
single_file="false"
restore="true"

while [ $# -gt 0 ]; do
  case "$1" in
    --rid) rid=${2:?--rid needs a value}; shift 2 ;;
    --rid=*) rid=${1#*=}; shift ;;
    --output) output=${2:?--output needs a value}; shift 2 ;;
    --output=*) output=${1#*=}; shift ;;
    --configuration) configuration=${2:?--configuration needs a value}; shift 2 ;;
    --configuration=*) configuration=${1#*=}; shift ;;
    --no-self-contained) self_contained="false"; shift ;;
    --single-file) single_file="true"; shift ;;
    --no-restore) restore="false"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'unknown option: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

if [ "$single_file" = "true" ] && [ "$self_contained" = "false" ]; then
  echo "--single-file requires a self-contained publish" >&2
  exit 2
fi

if [ -z "$rid" ]; then
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*|Windows_NT) os=win ;;
    Darwin) os=osx ;;
    *) os=linux ;;
  esac
  case "$(uname -m)" in
    arm64|aarch64) arch=arm64 ;;
    *) arch=x64 ;;
  esac
  rid="$os-$arch"
fi

case "$rid" in
  win-x64|win-arm64)   goos=windows; goarch=${rid#*-}; host_ext=".exe" ;;
  osx-x64|osx-arm64)   goos=darwin;  goarch=${rid#*-}; host_ext="" ;;
  linux-x64|linux-arm64) goos=linux; goarch=${rid#*-}; host_ext="" ;;
  *) echo "unsupported RID: $rid" >&2; exit 2 ;;
esac
case "$goarch" in
  x64) goarch=amd64 ;;
  arm64) goarch=arm64 ;;
esac

project="$root/desktop/avalonia/src/Reasonix.Studio.Shell/Reasonix.Studio.Shell.csproj"
if [ -z "$output" ]; then
  output="$root/desktop/avalonia/bin/publish/$rid"
fi
mkdir -p "$output"

if [ "$restore" = "true" ]; then
  dotnet restore "$project" --runtime "$rid"
fi

publish_args=(
  publish "$project"
  --configuration "$configuration"
  --runtime "$rid"
  --self-contained "$self_contained"
  --output "$output"
  --no-restore
)
if [ "$single_file" = "true" ]; then
  publish_args+=(-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true)
fi
dotnet "${publish_args[@]}"

host_name="reasonix-studio-host$host_ext"
(
  cd "$root"
  CGO_ENABLED=0 GOOS="$goos" GOARCH="$goarch" \
    go build -trimpath -o "$output/$host_name" ./cmd/reasonix-studio-host
)

test -s "$output/$host_name" || { echo "kernel host missing: $output/$host_name" >&2; exit 1; }
if [ "$single_file" = "true" ]; then
  shell_name="Reasonix.Studio.Shell$host_ext"
  test -s "$output/$shell_name" || { echo "shell executable missing: $output/$shell_name" >&2; exit 1; }
else
  test -s "$output/Reasonix.Studio.Shell.dll" || { echo "shell assembly missing: $output/Reasonix.Studio.Shell.dll" >&2; exit 1; }
fi

echo "Avalonia Studio published to $output ($rid, $configuration)"
