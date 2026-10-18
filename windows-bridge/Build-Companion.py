#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-2.0-or-later
"""Build the single user-facing EXE, embedding runtime payload and complete source.
Requires clean, committed source, compiled native/worker prebuilt files, official
net48 reference assemblies, and either Windows csc or Mono mcs. No SDK is shipped.
"""
import argparse, hashlib, io, json, os, subprocess, tempfile, zipfile
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--refs',type=Path,required=True)
p.add_argument('--csc')
p.add_argument('--mono',default='mono')
p.add_argument('--mono-config',type=Path)
p.add_argument('--mcs',default='/usr/lib/mono/4.5/mcs.exe')
p.add_argument('--test-source',type=Path,help='Test-only archive; release builds must omit this option')
p.add_argument('--chroma-profile',type=Path,action='append',default=[],help='Additional local Chroma exports to check without embedding them')
a=p.parse_args(); root=Path(__file__).resolve().parent; repo=root.parent
files=['Common.ps1','Install.ps1','Uninstall.ps1','Collect-Driver.ps1','PACKAGE-SHA256.json','prebuilt/Split65Bridge.exe','prebuilt/driver/Split65LampBridge.dll','prebuilt/driver/Split65LampBridge.inf']
with io.BytesIO() as mem:
 with zipfile.ZipFile(mem,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
  for name in files:
   data=(root/name).read_bytes()
   if name.endswith('.ps1'):data=data.replace(b'\r\n',b'\n').replace(b'\n',b'\r\n')
   z.writestr(name,data)
  z.writestr('firmware/split65-dynamic-lighting-v0.5.0.bin',(repo/'firmware/split65-dynamic-lighting-v0.5.0.bin').read_bytes())
 payload=mem.getvalue()
if a.test_source:source=a.test_source.read_bytes()
else:
 dirty=subprocess.check_output(['git','status','--porcelain'],cwd=repo,text=True)
 if dirty.strip():p.error('Commit corresponding source and prebuilt binaries before a release build.')
 source=subprocess.check_output(['git','archive','--format=zip','HEAD'],cwd=repo)
refs=['mscorlib','System','System.Core','System.Xml','System.Windows.Forms','System.Drawing','System.Web.Extensions','System.IO.Compression','System.IO.Compression.FileSystem']
compiler=([a.csc] if a.csc else [a.mono]+(['--config',str(a.mono_config.resolve())] if a.mono_config else [])+[a.mcs])
output=root/'dist'/'Split65-Companion.exe';output.parent.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='split65-companion-build-') as t:
 temp=Path(t);(temp/'Payload.zip').write_bytes(payload);(temp/'Source.zip').write_bytes(source)
 (temp/'PayloadInfo.cs').write_text('namespace Split65Companion { internal static class PayloadInfo { internal const string Hash="'+hashlib.sha256(payload).hexdigest()+'"; } }')
 flags=['-noconfig','-nostdlib+','-langversion:5','-platform:x64','-target:winexe','-optimize+','-out:'+str(output)]
 flags+=['-r:'+str(a.refs.resolve()/(name+'.dll')) for name in refs]
 flags+=['-resource:'+str(temp/'Payload.zip')+',Payload.zip','-resource:'+str(temp/'Source.zip')+',Source.zip']
 subprocess.run(compiler+flags+[str(root/'companion/CompanionCore.cs'),str(root/'companion/Companion.cs'),str(root/'companion/I18n.cs'),str(root/'companion/ChromaLayout.cs'),str(temp/'PayloadInfo.cs')],check=True)
 tests=temp/'CompanionTests.exe'
 test_flags=['-noconfig','-nostdlib+','-langversion:5','-platform:x64','-main:CompanionTests','-out:'+str(tests)]
 test_flags+=['-r:'+str(a.refs.resolve()/(name+'.dll')) for name in refs]
 subprocess.run(compiler+test_flags+[str(root/'companion/CompanionCore.cs'),str(root/'companion/ChromaLayout.cs'),str(root/'tests/CompanionTests.cs'),str(root/'tests/ChromaLayoutTests.cs')],check=True)
 runner=([str(tests)] if a.csc else [a.mono]+(['--config',str(a.mono_config.resolve())] if a.mono_config else [])+[str(tests)])
 subprocess.run(runner+[str(path.resolve()) for path in a.chroma_profile],check=True)
metadata={'version':'0.5.0','interface_languages':['zh-CN','en'],'repository_url':'https://github.com/YRN-playmaker/Epomaker_Split65-Win_Dynamic_Lighting_Effects','exe_sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'payload_sha256':hashlib.sha256(payload).hexdigest(),'source_zip_sha256':hashlib.sha256(source).hexdigest(),'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip(),'real_windows_tested':False}
(output.parent/'Companion-build.json').write_text(json.dumps(metadata,indent=2)+'\n')
print(json.dumps({'path':str(output),'size':output.stat().st_size,**metadata},indent=2))
