import re

file_path = r'C:\Users\Vadym\Desktop\tcp nulol prosto\ProtoFiless\StubMessages.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# I will find the class GetOtherPlayerItemsRequest and move it inside.
pattern = r'(public sealed partial class GetOtherPlayerItemsRequest.*?)(\n\s*\}|\Z)'

# First, I'll remove any trailing } that were added at the end
content = content.rstrip(' \n\r\t}')

# Let's remove the classes from the end and put them right before the LAST } in the file.
classes_pattern = r'(public sealed partial class GetOtherPlayerItemsRequest[\s\S]*)(?=\Z)'
match = re.search(classes_pattern, content)
if match:
    classes = match.group(1)
    content = content[:match.start()]
    # Ensure there's a namespace closing brace
    content = content.rstrip()
    if content.endswith('}'):
        content = content[:-1] # remove last brace
    content += '\n' + classes + '\n}\n'

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
