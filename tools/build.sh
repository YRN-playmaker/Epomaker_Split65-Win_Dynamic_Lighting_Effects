#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
build_dir="${1:-$project_dir/../qmk-split65-build}"
base_commit=c64b964e0785a0345e8901fc15120a607cface9b
if [[ ! -d "$build_dir/.git" ]]; then
    git clone --no-checkout https://github.com/SRGBmods/EpomakerQMK.git "$build_dir"
    git -C "$build_dir" checkout --detach "$base_commit"
fi
if [[ "$(git -C "$build_dir" rev-parse HEAD)" != "$base_commit" ]]; then
    echo 'Wrong QMK commit. Use a fresh build directory.' >&2
    exit 1
fi
git -C "$build_dir" submodule update --init --depth 1 lib/chibios lib/chibios-contrib lib/printf lib/lufa
# WB32 control-transfer fixes are gated to SPLIT65_LAMPARRAY_ENABLE.
contrib_dir="$build_dir/lib/chibios-contrib"
if ! git -C "$contrib_dir" apply --reverse --check "$project_dir/dynamic_lighting/wb32-ep0.patch" 2>/dev/null; then
    git -C "$contrib_dir" apply --check "$project_dir/dynamic_lighting/wb32-ep0.patch"
    git -C "$contrib_dir" apply "$project_dir/dynamic_lighting/wb32-ep0.patch"
fi
if ! git -C "$build_dir" apply --reverse --check "$project_dir/dynamic_lighting/qmk-core.patch" 2>/dev/null; then
    # Recognize and remove our exact older patch before updating an existing
    # dedicated build checkout. Unrelated patches still fail the checks below.
    if git -C "$build_dir" apply --reverse --check "$project_dir/dynamic_lighting/qmk-core-v0.4.5.patch" 2>/dev/null; then
        git -C "$build_dir" apply --reverse "$project_dir/dynamic_lighting/qmk-core-v0.4.5.patch"
    elif git -C "$build_dir" apply --reverse --check "$project_dir/dynamic_lighting/qmk-core-v0.4.2.patch" 2>/dev/null; then
        git -C "$build_dir" apply --reverse "$project_dir/dynamic_lighting/qmk-core-v0.4.2.patch"
    elif git -C "$build_dir" apply --reverse --check "$project_dir/dynamic_lighting/qmk-core-v0.4.1.patch" 2>/dev/null; then
        git -C "$build_dir" apply --reverse "$project_dir/dynamic_lighting/qmk-core-v0.4.1.patch"
    fi
    git -C "$build_dir" apply --check "$project_dir/dynamic_lighting/qmk-core.patch"
    git -C "$build_dir" apply "$project_dir/dynamic_lighting/qmk-core.patch"
fi
python3 "$project_dir/tools/install_keyboard.py" "$project_dir" "$build_dir"
mkdir -p "$project_dir/firmware"
cd "$build_dir"
make SKIP_GIT=yes BREAK_ON_ERRORS=yes epomaker/split65:default
cp epomaker_split65_default.bin "$project_dir/firmware/split65-dynamic-lighting-v0.4.8.bin"
# A separate keymap target avoids stale objects built with different defines.
mkdir -p keyboards/epomaker/split65/keymaps/inputcheck
cp keyboards/epomaker/split65/keymaps/default/{keymap.c,config.h,rules.mk} keyboards/epomaker/split65/keymaps/inputcheck/
echo 'DYNAMIC_LIGHTING_ENABLE = no' >> keyboards/epomaker/split65/keymaps/inputcheck/rules.mk
make SKIP_GIT=yes BREAK_ON_ERRORS=yes epomaker/split65:inputcheck
cp epomaker_split65_inputcheck.bin "$project_dir/firmware/split65-input-check-v0.4.8.bin"
python3 "$project_dir/tests/check_linked_usb.py" "$build_dir"
python3 "$project_dir/tools/record_build.py" "$project_dir"
echo 'Built both images. No device has been flashed.'
