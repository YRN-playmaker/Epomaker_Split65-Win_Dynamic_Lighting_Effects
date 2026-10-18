#!/usr/bin/env python3
"""Refresh image sizes/checksums without claiming hardware validation."""
from pathlib import Path
import hashlib
import json
import sys

project = Path(sys.argv[1])
manifest_path = project / 'docs/build-manifest.json'
manifest = json.loads(manifest_path.read_text())
manifest['firmware'] = {}
checksums = []
for image in sorted((project / 'firmware').glob('*.bin')):
    digest = hashlib.sha256(image.read_bytes()).hexdigest()
    manifest['firmware'][image.name] = {'size_bytes': image.stat().st_size, 'sha256': digest}
    checksums.append(f'{digest}  {image.name}')
(project / 'firmware/SHA256SUMS').write_text('\n'.join(checksums) + '\n')
manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
