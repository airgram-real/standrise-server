import os
import re

file_path = r'C:\Users\Vadym\Desktop\tcp nulol prosto\ProtoFiless\StubMessages.cs'

with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# find first occurrence of GetOtherPlayerItemsRequest
idx = content.find('public sealed partial class GetOtherPlayerItemsRequest')
if idx != -1:
    content = content[:idx]
    # remove trailing whitespace and ensure it ends with }
    content = content.rstrip()
    if not content.endswith('}'):
        content += '\n}'

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
