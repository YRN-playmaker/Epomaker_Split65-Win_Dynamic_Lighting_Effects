#!/usr/bin/env python3
"""Verify the real EXE payload and package bilingual GitHub Release assets."""
from pathlib import Path
import hashlib, io, json, shutil, subprocess, zipfile

repo=Path(__file__).resolve().parents[1]
out=repo/'windows-bridge/dist'
exe=out/'Split65-Companion.exe'
data=exe.read_bytes()
meta=json.loads((out/'Companion-build.json').read_text())
assert hashlib.sha256(data).hexdigest()==meta['exe_sha256']
archives=[]
for end in [i for i in range(len(data)) if data.startswith(b'PK\x05\x06',i)]:
    size=int.from_bytes(data[end+12:end+16],'little')
    offset=int.from_bytes(data[end+16:end+20],'little')
    comment=int.from_bytes(data[end+20:end+22],'little')
    raw=data[end-size-offset:end+22+comment]
    try:
        z=zipfile.ZipFile(io.BytesIO(raw))
        if z.testzip() is None: archives.append((raw,z))
    except zipfile.BadZipFile: pass
payload=next((raw,z) for raw,z in archives if 'PACKAGE-SHA256.json' in z.namelist())
source=next((raw,z) for raw,z in archives if 'windows-bridge/companion/I18n.cs' in z.namelist())
assert hashlib.sha256(payload[0]).hexdigest()==meta['payload_sha256']
assert hashlib.sha256(source[0]).hexdigest()==meta['source_zip_sha256']
assert source[0]==subprocess.check_output(['git','archive','--format=zip','HEAD'],cwd=repo)
manifest=json.loads(payload[1].read('PACKAGE-SHA256.json'))
for entry in manifest['files']:
    actual=payload[1].read(entry['path'])
    assert hashlib.sha256(actual).hexdigest()==entry['sha256']
    assert actual==(repo/'windows-bridge'/entry['path']).read_bytes()
firmware='firmware/split65-dynamic-lighting-v0.5.0.bin'
assert payload[1].read(firmware)==(repo/firmware).read_bytes()
assert meta['interface_languages']==['zh-CN','en']
assert meta['repository_url']=='https://github.com/YRN-playmaker/Epomaker_Split65-Win_Dynamic_Lighting_Effects'
assets=[]
with zipfile.ZipFile(out/('Split65-Companion-v'+meta['version']+'-Candidate-zh-en.zip'),'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    z.write(exe,'Split65-Companion.exe')
    z.write(repo/'releases/README-中文-English.txt','README-中文-English.txt')
    z.write(repo/firmware,Path(firmware).name)
assets.append(out/('Split65-Companion-v'+meta['version']+'-Candidate-zh-en.zip'))
src=out/('Split65-Source-v'+meta['version']+'-Candidate.zip');src.write_bytes(source[0]);assets.append(src)
fw=out/Path(firmware).name;shutil.copy2(repo/firmware,fw);assets.append(fw)
(out/'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in assets))
print('PASS: embedded payload, exact committed source, firmware/native integrity, matching worker and bilingual package integrity')
for p in assets: print(p.name,p.stat().st_size)
