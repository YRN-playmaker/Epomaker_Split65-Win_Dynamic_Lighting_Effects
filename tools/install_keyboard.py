#!/usr/bin/env python3
"""Install only keyboard sources, leaving tests/docs outside the QMK build."""
from pathlib import Path
import shutil
import sys

source, qmk = map(Path, sys.argv[1:])
target = qmk / 'keyboards/epomaker/split65'
for name in ('config.h', 'halconf.h', 'mcuconf.h', 'keyboard.json', 'post_rules.mk', 'rgb_matrix_user.inc'):
    shutil.copy2(source / name, target / name)
for name in ('rgb_record', 'wls', 'linker', 'keymaps', 'dynamic_lighting'):
    shutil.copytree(source / name, target / name, dirs_exist_ok=True)
shutil.copy2(source / 'epomaker_split65.c', target / 'split65.c')
