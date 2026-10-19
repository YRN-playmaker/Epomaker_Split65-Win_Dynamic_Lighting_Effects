# Build and checks

English | [中文](BUILD.md)

Validated environment: Linux, QMK CLI 1.2.0, ARM GCC 13.2.1 20231009. This requires Git, Python 3, GNU Make, the ARM bare-metal toolchain and newlib; protocol tests also require a host GCC. Build in a WSL/Linux environment with these tools. The Windows check CMD does not need the build environment.

In a dedicated Python virtual environment, install the pinned CLI:

```sh
python3 -m venv .venv
. .venv/bin/activate
python3 -m pip install qmk==1.2.0
bash tools/build.sh /absolute/path/to/a/dedicated/qmk-build
```

The toolchain must be on `PATH`. The script pins SRGBmods/EpomakerQMK at c64b964e and its four submodules, applies this project's QMK/WB32 patches, copies the keyboard source, then builds `default` and `inputcheck`. Pinned versions, dependencies and checks for the delivered images are in `build-manifest.json`. Different compilers may produce different bytes; identical SHA256 across toolchains is not claimed.

Do not use a personal QMK checkout with local changes as the build directory. The script updates the keyboard source in a dedicated directory and recognizes only this project's older patches. The build only produces files; it does not connect to a device or flash automatically.

```sh
bash tests/run.sh /absolute/path/to/qmk-build
node tests/check_wireless_rgb.cjs
python3 tests/check_linked_usb.py /absolute/path/to/qmk-build
```

Optional local checks for the diagnostics tools use PowerShell 7 (test environment only):

```sh
pwsh -NoProfile -File tests/check_chroma_mapping.ps1
pwsh -NoProfile -File tests/check_deep_diagnostics.ps1
```

The user-facing CMD explicitly invokes Windows PowerShell 5.1. Script syntax and simulation tests on Linux cannot replace real Windows WinRT, ETW or hardware validation. The user has confirmed in the current Chroma profile that the separated layout removed the coupling; local software checks cannot extend that hardware validation.
