#!/usr/bin/env bash
set -euo pipefail
bridge_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
repo_dir="$(dirname "$bridge_dir")"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
gcc -std=c11 -Wall -Wextra -Werror -g -fsanitize=address,undefined \
    "$bridge_dir/core/bridge_core.c" "$repo_dir/dynamic_lighting/protocol.c" "$bridge_dir/tests/core_test.c" -o "$test_dir/core"
ASAN_OPTIONS=detect_leaks=0 "$test_dir/core"
python3 "$bridge_dir/tests/lifecycle_test.py"
# Optional: mcs/mono or Windows csc compile ClientTests with the production files.
if command -v mcs >/dev/null && command -v mono >/dev/null; then
    mcs -langversion:5 -platform:x64 -main:ClientTests -out:"$test_dir/client-tests.exe" \
        "$bridge_dir/client/BridgeClient.cs" "$bridge_dir/client/RootDevice.cs" "$bridge_dir/client/DriverAccess.cs" "$bridge_dir/client/RadioProtocol.cs" "$bridge_dir/tests/ClientTests.cs" "$bridge_dir/tests/ProtocolTests.cs"
    mono "$test_dir/client-tests.exe"
fi
