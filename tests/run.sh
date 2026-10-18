#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
cc_args=(-std=c11 -Wall -Wextra -Werror -g -fsanitize=address,undefined -I "$project_dir/dynamic_lighting")
gcc "${cc_args[@]}" "$project_dir/dynamic_lighting/protocol.c" "$project_dir/tests/protocol_test.c" -o "$test_dir/protocol"
ASAN_OPTIONS=detect_leaks=0 "$test_dir/protocol"
gcc "${cc_args[@]}" -I "$project_dir/tests/stubs" -DQMK_KEYBOARD_H='"mock.h"' \
    "$project_dir/dynamic_lighting/protocol.c" "$project_dir/tests/adapter_test.c" -o "$test_dir/adapter"
ASAN_OPTIONS=detect_leaks=0 "$test_dir/adapter"
gcc "${cc_args[@]}" -I "$project_dir/tests/stubs" -DQMK_KEYBOARD_H='"mock.h"' \
    -DSPLIT65_TEST_SLAVE_V044 "$project_dir/dynamic_lighting/protocol.c" "$project_dir/tests/adapter_test.c" -o "$test_dir/adapter-v044-target"
ASAN_OPTIONS=detect_leaks=0 "$test_dir/adapter-v044-target"
python3 "$project_dir/tools/generate_lamp_descriptor.py" --check
python3 "$project_dir/tests/check_descriptor.py"
python3 "$project_dir/tests/check_hardware_config.py"

python3 "$project_dir/tests/check_module.py"
python3 "$project_dir/tests/check_split_commands.py"
python3 "$project_dir/tests/check_split_wakeup.py"
if [[ $# -gt 0 ]]; then
    python3 "$project_dir/tests/check_wb32_ep0.py" "$1"
    python3 "$project_dir/tests/check_wireless_state.py" "$1"
    python3 "$project_dir/tests/check_wireless_payload.py" "$1"
    python3 "$project_dir/tests/check_usb_hook.py" "$1"
    python3 "$project_dir/tests/check_transport_release.py" "$1"
fi
