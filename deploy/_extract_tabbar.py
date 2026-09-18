from pathlib import Path
import re
t = Path(r'C:\Users\Administrator\Downloads\Portals.html').read_text(encoding='utf-8', errors='ignore')
titles = re.findall(r'class="_title_122x4_475">([^<]+)</div>', t)
Path(r'C:\StandRise\deploy\_tabbar_titles.txt').write_text('\n'.join(titles), encoding='utf-8')
i = t.find('fixed right-0 bottom-0')
Path(r'C:\StandRise\deploy\_tabbar_slice.html').write_text(t[i:i+14000], encoding='utf-8')
css = re.findall(r'\._[A-Za-z]+_122x4_[0-9]+\{[^}]+\}', t)
Path(r'C:\StandRise\deploy\_tabbar_css.txt').write_text('\n'.join(css[:120]), encoding='utf-8')
