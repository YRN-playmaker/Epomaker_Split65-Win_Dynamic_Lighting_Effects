# Split65 Companion 0.5.0 candidate

English | [中文](README.md)

The same bilingual EXE provides driver installation/update, the wireless bridge, Chroma layout repair, source/firmware/log export and uninstall.

Flash the supplied 0.5.0 BIN onto the left half first, then run the new EXE and enable dynamic lighting. An older driver prompts for administrator approval to update. The input scheduler fix is in the firmware; the existing right-half 0.4.9 firmware remains compatible and the receiver is not flashed.

This build adds a priority input lane, pre-USB state cleanup, 255-level intensity composition and a 100 ms wireless scheduling target with at most two packets in flight. See the [English release notes](../releases/v0.5.0-candidate.en.md) for the complete changes and test scope. Real input, gradients and wired control still need hardware validation; 10 fps is not guaranteed.

## Installation scope

Supports Windows 11 24H2/25H2 x64, build 26100+. Normal operation does not require administrator rights; install, repair and uninstall request UAC through the same EXE.

- Driver installation: `%ProgramData%\Split65LampBridge`. The UMDF2 DLL runs in user mode; inbox VHF / WUDFRd provide the lower layer, and no custom SYS is distributed.
- The administrator step extracts embedded files to a temporary installation directory writable by administrators and SYSTEM, verifies the prebuilt files, installs them, and cleans up the cache at the end.
- User cache and logs: `%LOCALAPPDATA%\Split65Companion`. Embedded runtime files are verified against their contents and restored if needed; the original EXE embeds the complete source ZIP.
- Creates a machine-unique one-year certificate for code signing only and adds it to LocalMachine Root / TrustedPublisher; the private key is deleted after signing, and uninstall removes the certificate by the exact thumbprint in the receipt. No shared private key is distributed.
- Binds only `ROOT\Split65LampBridge`; does not replace the physical keyboard, mouse or receiver driver, does not add keyboard/mouse input collections, and does not modify firmware, EEPROM or system class filters.
- Does not modify Secure Boot, test-signing mode or code-integrity settings. If an existing system policy rejects this development build, the original error is shown and the operation stops.
- If a reboot is required, it is shown explicitly; after reboot, open the same EXE and enable again. A failed repair attempts rollback from the current receipt and keeps the error record.

Local self-signing is not a Microsoft or vendor release signature. This program is a development candidate and has not passed HLK/WHQL/Razer certification.

## Development build

The complete corresponding source is embedded in the EXE and can be obtained through Export Source. On an x64 Developer PowerShell with VS 2022 C++ and SDK/WDK 26100 installed, `Build-Windows.ps1` builds the driver and worker; normal users do not need these tools.

On Linux, `Build-Client-Cross.py` uses Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3 and forces `-noconfig -nostdlib+` to avoid APIs added by the Mono runtime. `Build-Companion.py` supports Windows csc or Mono mcs; commit the clean source corresponding to the prebuilt driver/worker before embedding `git archive HEAD` into the final EXE. Output goes to `dist/` and is not committed, to avoid recursive embedding.

```text
python windows-bridge/Build-Companion.py --refs <Microsoft net48 reference assembly directory> --csc <Windows csc.exe>
```

For cross-platform builds, use `--mono`, `--mono-config` and `--mcs` instead of `--csc`. Underlying checks are in `tests/`; the GUI itself still needs real Windows validation. The original CMD and PS1 files remain in the source as internal build/debug entry points; release users do not need them.

## Technical basis and license

- [Microsoft UMDF filter installation](https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/installing-a-umdf-filter-driver): device-specific UpperFilters / WUDFRd.
- [Microsoft PnPUtil](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax): a successful add does not guarantee that the driver is selected.
- [Microsoft UpdateDriverForPlugAndPlayDevices](https://learn.microsoft.com/en-us/windows/win32/api/newdev/nf-newdev-updatedriverforplugandplaydevicesw): updates a driver by exact hardware ID.
- [Microsoft VHF_CONFIG](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/ns-vhf-_vhf_config) / [DMF reference](https://github.com/microsoft/DMF/blob/f9da490cd6e7a216f431ad245255548ab020d4f0/Dmf/Modules.Library/Dmf_VirtualHidDeviceVhf.c): user-mode FileHandle / local WDFIOTARGET path.
- [Microsoft RP2040 LampArray sample](https://github.com/microsoft/RP2040MacropadHidSample): reuses the verified 68-lamp standard reports, key bindings and geometry descriptions.

New source is GPL-2.0-or-later; the combined project and upstream licenses are provided in full in the source ZIP. The virtual VID:PID 342D:E4C8 is a temporary experimental identity and is not claimed to be assigned. This project is not affiliated with Microsoft, EPOMAKER or Razer.
