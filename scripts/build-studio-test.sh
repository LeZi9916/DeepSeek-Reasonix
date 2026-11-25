#!/usr/bin/env bash
set -euo pipefail

# One-click Studio test build: the Avalonia frontend and the Go kernel backend
# into desktop/avalonia/bin/test-build/<date>-<shortsha>/<platform>/, matching
# the layout of the verification records already kept there.

usage() {
  cat <<'EOF'
Usage: scripts/build-studio-test.sh [OPTIONS]

Build the Avalonia shell and the Go kernel host into one test-build directory,
then run both dotnet test projects and record the outcome beside the build.

Options:
  --rid RID          dotnet RID: win-x64, win-arm64, osx-x64, osx-arm64,
                     linux-x64, linux-arm64. Defaults to the host platform.
  --dir DIR          Override the test-build directory.
  --configuration CFG Build configuration. Default: Release.
  --skip-tests       Build only; do not run the test projects.
  --no-restore       Skip dotnet restore (publish + solution).
  -h, --help         Show this help.

The directory is desktop/avalonia/bin/test-build/<date>-<shortsha>/<platform>/
unless --dir is given. The Go host is cross-compiled with CGO disabled and
placed beside the shell so HostLauncher.Locate finds it at startup.
EOF
}

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
rid=""
dir=""
configuration="Release"
run_tests="true"
restore="true"

while [ $# -gt 0 ]; do
  case "$1" in
    --rid) rid=${2:?--rid needs a value}; shift 2 ;;
    --rid=*) rid=${1#*=}; shift ;;
    --dir) dir=${2:?--dir needs a value}; shift 2 ;;
    --dir=*) dir=${1#*=}; shift ;;
    --configuration) configuration=${2:?--configuration needs a value}; shift 2 ;;
    --configuration=*) configuration=${1#*=}; shift ;;
    --skip-tests) run_tests="false"; shift ;;
    --no-restore) restore="false"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'unknown option: %s\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

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
  win-x64|win-arm64)     platform="windows-${rid#*-}"; host_ext=".exe" ;;
  osx-x64|osx-arm64)     platform="darwin-${rid#*-}";  host_ext="" ;;
  linux-x64|linux-arm64) platform="linux-${rid#*-}";   host_ext="" ;;
  *) echo "unsupported RID: $rid" >&2; exit 2 ;;
esac

solution="$root/desktop/avalonia/Reasonix.Studio.slnx"
if [ -z "$dir" ]; then
  stamp="$(date +%F)-$(git -C "$root" rev-parse --short=8 HEAD 2>/dev/null || echo unknown)"
  dir="$root/desktop/avalonia/bin/test-build/$stamp/$platform"
fi
mkdir -p "$dir/verification"

# Frontend (self-contained Avalonia publish) and backend (Go host) share the
# output directory; the publish script owns the shared build flags.
publish_args=(--rid "$rid" --output "$dir" --configuration "$configuration")
if [ "$restore" = "false" ]; then
  publish_args+=(--no-restore)
fi
bash "$root/scripts/build-studio-avalonia.sh" "${publish_args[@]}"

host_name="reasonix-studio-host$host_ext"
test -s "$dir/$host_name" || { echo "kernel host missing: $dir/$host_name" >&2; exit 1; }

build_log="$dir/verification/reasonix-build.log"
validation="$dir/verification/validation.json"
readme="$dir/verification/README.txt"
render_log="$dir/verification/reasonix-render-tests.log"
baseline_log="$dir/verification/reasonix-baseline-tests.log"
render_status="not run"
baseline_status="not run"
build_status="pass"

if [ "$restore" = "true" ]; then
  dotnet restore "$solution"
fi

set +e
dotnet build "$solution" -c "$configuration" --no-restore -v:minimal > "$build_log" 2>&1
build_rc=$?
set -e
if [ "$build_rc" -ne 0 ]; then
  build_status="fail"
fi

# The tests execute on this machine, not on the target RID. Detect the runner's
# platform for the test host, even when --skip-tests leaves it unused.
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*|Windows_NT) test_goos=windows; test_host_ext=".exe" ;;
  Darwin) test_goos=darwin;  test_host_ext="" ;;
  *) test_goos=linux; test_host_ext="" ;;
esac
case "$(uname -m)" in
  arm64|aarch64) test_goarch=arm64 ;;
  *) test_goarch=amd64 ;;
esac
test_host_name="reasonix-studio-host$test_host_ext"

if [ "$run_tests" = "true" ] && [ "$build_rc" -eq 0 ]; then
  # A net10.0 testhost can use a newer installed runtime when 10.0 is absent,
  # which is the usual case in WSL. Build the host for the runner, not the RID.
  (
    cd "$root"
    CGO_ENABLED=0 GOOS="$test_goos" GOARCH="$test_goarch" \
      go build -trimpath -o "$dir/verification/$test_host_name" ./cmd/reasonix-studio-host
  )

  export DOTNET_ROLL_FORWARD=Major
  for project in Reasonix.Studio.Render.Tests Reasonix.Studio.Baseline.Tests; do
    target="$root/desktop/avalonia/tests/$project/bin/$configuration/net10.0"
    mkdir -p "$target"
    cp "$dir/verification/$test_host_name" "$target/$test_host_name"
  done

  set +e
  dotnet test "$root/desktop/avalonia/tests/Reasonix.Studio.Render.Tests/Reasonix.Studio.Render.Tests.csproj" \
    -c "$configuration" --no-build --no-restore > "$render_log" 2>&1
  render_status=$?
  dotnet test "$root/desktop/avalonia/tests/Reasonix.Studio.Baseline.Tests/Reasonix.Studio.Baseline.Tests.csproj" \
    -c "$configuration" --no-build --no-restore > "$baseline_log" 2>&1
  baseline_status=$?
  set -e
fi

commit=$(git -C "$root" rev-parse HEAD 2>/dev/null || echo unknown)
cat > "$validation" <<EOF
{
  "date": "$(date +%F)",
  "commit": "$commit",
  "rid": "$rid",
  "platform": "$platform",
  "configuration": "$configuration",
  "build": "$build_status",
  "renderTests": "$render_status",
  "baselineTests": "$baseline_status",
  "skipTests": $([ "$run_tests" = "true" ] && echo false || echo true)
}
EOF

{
  echo "Verification record for commit $commit."
  echo
  echo "Commands run from the repository root:"
  echo
  echo "  bash scripts/build-studio-avalonia.sh --rid $rid --output <this directory> --configuration $configuration"
  echo "    Frontend: self-contained Avalonia publish. Backend: reasonix-studio-host cross-compiled with CGO disabled."
  echo
  echo "  dotnet build desktop/avalonia/Reasonix.Studio.slnx -c $configuration --no-restore -v:minimal"
  echo "    Result: $build_status. See reasonix-build.log."
  echo
  if [ "$run_tests" = "true" ] && [ "$build_rc" -eq 0 ]; then
    echo "  dotnet test desktop/avalonia/tests/Reasonix.Studio.Render.Tests/Reasonix.Studio.Render.Tests.csproj -c $configuration --no-build --no-restore"
    echo "    Exit code: $render_status. See reasonix-render-tests.log."
    echo
    echo "  dotnet test desktop/avalonia/tests/Reasonix.Studio.Baseline.Tests/Reasonix.Studio.Baseline.Tests.csproj -c $configuration --no-build --no-restore"
    echo "    Exit code: $baseline_status. See reasonix-baseline-tests.log."
  else
    echo "  Tests were not run (--skip-tests, or the build failed)."
  fi
  echo
  echo "The test-runner host ($test_host_name, ${test_goos}/${test_goarch}) is placed"
  echo "beside each test assembly for HostLauncher.Locate. DOTNET_ROLL_FORWARD=Major"
  echo "lets the net10.0 testhost use a newer installed runtime when 10.0 is absent."
  echo "The published build's host is $host_name ($rid)."
} > "$readme"

if [ "$build_rc" -ne 0 ]; then
  echo "build failed; see $build_log" >&2
  exit 1
fi
if [ "$run_tests" = "true" ] && { [ "$render_status" -ne 0 ] || [ "$baseline_status" -ne 0 ]; }; then
  echo "tests failed; see $render_log and $baseline_log" >&2
  exit 1
fi

echo "Studio test build ready: $dir"
echo "Verification: $dir/verification"
