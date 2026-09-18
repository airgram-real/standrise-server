import os
import re

file_path = r'C:\Users\Vadym\Desktop\tcp nulol prosto\ProtoFiless\StubMessages.cs'

with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Find the second occurrence of the header comment
search_str = '// Stub protobuf message types needed by various RemoteServices.'
first_idx = content.find(search_str)
if first_idx != -1:
    second_idx = content.find(search_str, first_idx + 1)
    if second_idx != -1:
        # Cut the content right before the second occurrence
        content = content[:second_idx]
        
# Ensure it ends with closing bracket for namespace
content = content.rstrip()
if content.endswith('}'):
    pass
else:
    # try to append closing brace
    content += '\n}'

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
