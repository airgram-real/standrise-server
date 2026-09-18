import os
import re

file_path = r'C:\Users\Vadym\Desktop\tcp nulol prosto\ProtoFiless\StubMessages.cs'

with open(file_path, 'rb') as f:
    content = f.read()

# remove null bytes and weird powershell echo stuff
content = content.replace(b'\x00', b'')

content_str = content.decode('utf-8', errors='ignore')
content_str = content_str.strip()

# ensure single closing brace
while content_str.endswith('}'):
    content_str = content_str[:-1].strip()
content_str += '\n}'

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content_str)
