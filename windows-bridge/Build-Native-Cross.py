#!/usr/bin/env python3
"""Build real x64 UMDF code with LLVM and extracted official SDK/WDK/MSVC.

The toolchain directory contains extracted NuGet packages named
microsoft.windows.sdk.cpp, microsoft.windows.sdk.cpp.x64, microsoft.windows.wdk.x64
and an msvc directory extracted from Microsoft's CRT Headers/Desktop VSIXs.
No Windows headers or runtime binaries are replaced by local stubs.
"""
import argparse, json, os, subprocess
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--toolchain', type=Path, required=True)
p.add_argument('--clang', required=True)
p.add_argument('--link', required=True)
a = p.parse_args()
t = a.toolchain.resolve()
root = Path(__file__).resolve().parent
sdk = t / 'microsoft.windows.sdk.cpp/c/Include/10.0.26100.0'
wdk = t / 'microsoft.windows.wdk.x64/c'
vc = next((t/'msvc').glob('Contents/VC/Tools/MSVC/*'))
headers = [vc/'include', sdk/'um', sdk/'shared', sdk/'ucrt', sdk/'winrt',
           wdk/'Include/wdf/umdf/2.15', wdk/'Include/10.0.26100.0/um',
           wdk/'Include/10.0.26100.0/shared']
out = root/'build'; out.mkdir(exist_ok=True)
# Windows includes are case-insensitive. Preserve exact Microsoft files while
# making their documented include spelling work on a case-sensitive build host.
entries = []
for directory in headers:
    for f in directory.rglob('*'):
        if f.is_file(): entries.append({'type':'file','name':str(f),'external-contents':str(f)})
overlay = out/'windows-headers.json'
overlay.write_text(json.dumps({'version':0,'case-sensitive':False,'roots':entries}))
libs = out/'libs'; libs.mkdir(exist_ok=True)
for directory in [vc/'lib/x64', t/'microsoft.windows.sdk.cpp.x64/c/um/x64',
                  t/'microsoft.windows.sdk.cpp.x64/c/ucrt/x64',
                  wdk/'Lib/wdf/umdf/x64/2.15',wdk/'Lib/10.0.26100.0/um/x64']:
    for f in directory.iterdir():
        if not f.is_file() or f.suffix.lower() != '.lib': continue
        target=libs/f.name.lower()
        if not target.exists(): target.symlink_to(f)
objects=[]
sources=[root/'driver/Split65LampBridge.c',root/'core/bridge_core.c',root.parent/'dynamic_lighting/protocol.c']
for source in sources:
    obj=out/(source.stem+'.obj');objects.append(str(obj))
    command=[a.clang,'--driver-mode=cl','/nologo','/c','/std:c11','/O2','/MT','/W3',
             '/DUNICODE','/D_UNICODE','/D_WIN32_WINNT=0x0A00','/DNTDDI_VERSION=0x0A000000',
             '/DUMDF_VERSION_MAJOR=2','/DUMDF_VERSION_MINOR=15',
             '/clang:--target=x86_64-pc-windows-msvc','/clang:-ivfsoverlay',
             '/clang:'+str(overlay),'/clang:-Wno-pragma-pack','/clang:-Wno-ignored-attributes',
             '/Fo'+str(obj)]
    command += ['/imsvc'+str(d) for d in headers]
    subprocess.run(command+['--',str(source)],check=True)
binary=root/'prebuilt/driver/Split65LampBridge.dll';binary.parent.mkdir(parents=True,exist_ok=True)
subprocess.run([a.link,'/nologo','/dll','/machine:x64','/subsystem:windows','/dynamicbase','/nxcompat',
                '/out:'+str(binary),'/libpath:'+str(libs),'/nodefaultlib:oldnames.lib',*objects,
                'wdfdriverstubum.lib','vhfum.lib','mincore.lib','ntdll.lib','advapi32.lib'],check=True)
print('Built x64 UMDF DLL from official SDK/WDK and static Microsoft CRT.')
