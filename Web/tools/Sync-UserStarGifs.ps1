# Импорт GIF звёзд из чата Cursor -> PNG для магазина
$ErrorActionPreference = 'Stop'
$chatAssets = 'C:\Users\Administrator\.cursor\projects\c-Users-Administrator-Desktop-StandRise-FULL-20260910-2203\assets'
$srcDir = 'C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\assets\icons\source'
$tmp = 'C:\tmp\ast'
New-Item -ItemType Directory -Force -Path $srcDir, $tmp | Out-Null
robocopy $chatAssets $tmp C_2CE3~1.GIF C__USE~4.GIF C_D62A~1.GIF /NFL /NDL | Out-Null
Copy-Item "$tmp\*13696*34c6fac0*.gif" "$srcDir\star-s.gif" -Force
Copy-Item "$tmp\*13697*978457eb*.gif" "$srcDir\star-m.gif" -Force
Copy-Item "$tmp\*13698*badd167f*.gif" "$srcDir\star-l.gif" -Force
python C:\StandRise\Web\tools\import_star_icons.py
Write-Host 'Stars + logo ok'
