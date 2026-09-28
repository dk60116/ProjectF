from pathlib import Path
import olefile
import struct
import zlib
import sys

sys.stdout.reconfigure(encoding='utf-8')

path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r'C:\Users\dk601\Downloads\붙임2.-참가-신청서-외-신청서류\붙임2. 참가 신청서(일반부).hwp')
ole = olefile.OleFileIO(path)
print('FILE', path)
print('STREAMS', ole.listdir())
for section in (stream for stream in ole.listdir() if stream[0] == 'BodyText'):
    data = zlib.decompress(ole.openstream(section).read(), -15)
    print('SECTION', section, 'bytes', len(data))
    pos = 0
    index = 0
    while pos < len(data):
        header = struct.unpack_from('<I', data, pos)[0]
        pos += 4
        tag = header & 1023
        size = header >> 20
        if size == 4095:
            size = struct.unpack_from('<I', data, pos)[0]
            pos += 4
        payload = data[pos:pos + size]
        pos += size
        if tag == 67:
            value = payload.decode('utf-16le', errors='replace').replace('\r', ' ').strip()
            if value:
                print(f'{index:04d}: {value[:4000]}')
            index += 1
