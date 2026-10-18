#!/usr/bin/env python3
"""Separate Chroma device matrices using an inferred 40-pixel cell model.

The model matches the native mouse LedWidth/LedHeight and predicts the observed
P collision and all five keyboard highlights in the user's WDL screenshot.
The user confirmed coupling cleared with the separated layout. The model is
still inferred; Chroma internals have not been inspected.
Device metadata, effects and targets are unchanged; only mouse X coordinates,
profile title/ID and layer IDs change. No HID/driver/firmware operations.
"""
import argparse
import copy
import itertools
import json
from pathlib import Path
import uuid


def collisions(profile, cell_size=40):
    pairs = []
    for a, b in itertools.combinations(profile['devices'], 2):
        def cells(device):
            return {(device['x'] + led['col'] * cell_size,
                     device['y'] + led['row'] * cell_size): led
                    for led in device['leds']}
        ca, cb = cells(a), cells(b)
        for point in sorted(set(ca) & set(cb)):
            pairs.append({'a': a['id'], 'b': b['id'], 'point': list(point),
                          'a_led': ca[point], 'b_led': cb[point]})
    return pairs


def separate(document, cell_size=40):
    if document.get('version') != 1 or len(document.get('profiles', [])) != 1:
        raise ValueError('Expected an observed version-1 single-profile export')
    output = copy.deepcopy(document)
    profile = output['profiles'][0]
    fixed = [d for d in profile['devices'] if d.get('category', '').lower() != 'mouse']
    moving = [d for d in profile['devices'] if d.get('category', '').lower() == 'mouse']
    if not fixed or not moving:
        raise ValueError('Expected keyboard and mouse device definitions')

    def right_edge(device):
        return max(device['x'] + device['width'],
                   device['x'] + device['config']['MatrixMaxCol'] * cell_size)

    cursor = max(right_edge(d) for d in fixed) + 8 * cell_size
    for mouse in moving:
        mouse['x'] = ((cursor + cell_size - 1) // cell_size) * cell_size
        cursor = right_edge(mouse) + 8 * cell_size
    if collisions(profile, cell_size):
        raise ValueError('Logical overlap remains; no output should be used')
    profile['id'] = uuid.uuid4().hex[:12]
    profile['title'] = 'Split65-Separated-Layout'

    def new_layer_ids(layers):
        for layer in layers:
            if 'id' in layer:
                layer['id'] = uuid.uuid4().hex[:12]
            if 'children' in layer:
                new_layer_ids(layer['children'])
    new_layer_ids(profile.get('layers', []))
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    if args.destination.exists():
        raise SystemExit('Refusing to replace an existing file')
    source = json.loads(args.source.read_text(encoding='utf-8-sig'))
    output = separate(source)
    args.destination.write_text(json.dumps(output, ensure_ascii=False, separators=(',', ':')),
                                encoding='utf-8')
    print('Inferred 40-pixel model, overlap pairs:',
          len(collisions(source['profiles'][0])), '->',
          len(collisions(output['profiles'][0])))
    print(args.destination)


if __name__ == '__main__':
    main()
