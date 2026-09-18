import json
from pathlib import Path

paths = [
    Path(r'C:\StandRise\bin\Release\net7.0\Data\StandRise.inventory_item_definition.json'),
    Path(r'C:\StandRise\data\StandRise.inventory_item_definition.json'),
    Path(r'C:\StandRise\build-out\Data\StandRise.inventory_item_definition.json'),
]
for p in paths:
    if not p.exists():
        continue
    data = json.loads(p.read_text(encoding='utf-8'))
    n = 0
    for d in data:
        if d.get('key') == 44007 or (d.get('displayName') or '') == 'AKR "2 Years Red"':
            props = d.setdefault('properties', {})
            old = props.get('value')
            props['value'] = 5
            n += 1
            print(p.name, 'key', d.get('key'), old, '->', 5)
    if n:
        p.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    print('updated', n, 'in', p)
