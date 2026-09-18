file_path = r'C:\Users\Vadym\Desktop\tcp nulol prosto\ProtoFiless\StubMessages.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# count { and }
open_count = content.count('{')
close_count = content.count('}')

diff = open_count - close_count
if diff > 0:
    content += '\n}' * diff

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
