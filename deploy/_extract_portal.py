from pathlib import Path
import re
t = Path(r'C:\Users\Administrator\Downloads\Portals.html').read_text(encoding='utf-8', errors='ignore')
out = Path(r'C:\StandRise\deploy\_portal_extract.txt')
lines = []
for s in ['fixed', 'bottom-0', 'nav', 'Магазин', 'Профиль', 'Инвентарь', 'Главная', 'Market', 'Home', 'Profile']:
    lines.append(f'FIND {s} {t.find(s)} {t.lower().find(s.lower())}')
fixes = re.findall(r'class="[^"]*bottom[^"]*"', t)
lines.append(f'bottom class count {len(fixes)}')
for f in fixes[:80]:
    lines.append(f)
# last 3000 chars of body
idx = t.find('<div id="root">')
lines.append('--- ROOT START 4000 ---')
lines.append(t[idx:idx+4000])
lines.append('--- ROOT END 8000 ---')
# look near end of root
end = t.find('</div>\n    <div id="portal">')
lines.append('--- BEFORE PORTAL 6000 ---')
lines.append(t[max(0,end-6000):end])
out.write_text('\n'.join(lines), encoding='utf-8')
print('wrote', out, 'len', len(t))
