#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Build against official net48 reference DLLs, never Mono's runtime API surface.
Extract Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3 NuGet package;
--refs is its build/.NETFramework/v4.8 directory. Set up Mono normally, or supply
its environment/library paths externally for a private extracted installation.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--refs', type=Path, required=True)
p.add_argument('--mono', default='mono')
p.add_argument('--mono-config', type=Path)
p.add_argument('--mcs', default='/usr/lib/mono/4.5/mcs.exe')
a = p.parse_args()
root = Path(__file__).resolve().parent
mono = [a.mono] + (['--config', str(a.mono_config.resolve())] if a.mono_config else [])
references = [a.refs.resolve() / name for name in ['mscorlib.dll', 'System.dll', 'System.Core.dll']]
for reference in references:
    if not reference.is_file():
        p.error('Missing Microsoft .NET Framework 4.8 reference: ' + str(reference))
compiler = mono + [a.mcs, '-noconfig', '-nostdlib+', '-langversion:5', '-platform:x64', '-optimize+']
compiler += ['-r:' + str(reference) for reference in references]
sources = [str(root / 'client' / name) for name in ['BridgeClient.cs', 'RootDevice.cs', 'DriverAccess.cs', 'RadioProtocol.cs']]
(root / 'prebuilt').mkdir(exist_ok=True)
subprocess.run(compiler + ['-out:' + str(root / 'prebuilt/Split65Bridge.exe')] + sources, check=True)
with tempfile.TemporaryDirectory(prefix='split65-net48-tests-') as temp:
    test = str(Path(temp) / 'ClientTests.exe')
    subprocess.run(compiler + ['-main:ClientTests', '-out:' + test] + sources + [str(root / 'tests/ClientTests.cs'),str(root / 'tests/ProtocolTests.cs')], check=True)
    subprocess.run(mono + [test], check=True)
manifest_path = root / 'PACKAGE-SHA256.json'
manifest = json.loads(manifest_path.read_text())
manifest['version'] = '0.5.0'
for file in manifest['files']:
    file['sha256'] = hashlib.sha256((root / file['path']).read_bytes()).hexdigest()
manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
print('Client and regression tests built against Microsoft .NET Framework 4.8 references.')
