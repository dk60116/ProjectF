from io import BytesIO
from pathlib import Path
import sys

from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import NameObject

source = Path(r'C:\Users\dk601\Downloads\붙임2.-참가-신청서-외-신청서류\붙임2. 참가 신청서(일반부).pdf')
quality = int(sys.argv[1]) if len(sys.argv) > 1 else 85
target = Path(r'C:\Git\ProjectF\tmp\indieu') / f'application_optimized_q{quality}.pdf'

writer = PdfWriter()
writer.clone_document_from_reader(PdfReader(source))
seen = set()
saved = 0
replaced = 0
for page in writer.pages:
    resources = page.get('/Resources') or {}
    objects = resources.get('/XObject') or {}
    for indirect in objects.values():
        key = (indirect.idnum, indirect.generation)
        if key in seen:
            continue
        seen.add(key)
        stream = indirect.get_object()
        if stream.get('/Subtype') != '/Image' or '/SMask' in stream:
            continue
        width = int(stream['/Width'])
        height = int(stream['/Height'])
        raw = stream.get_data()
        if len(raw) != width * height * 3:
            continue
        image = Image.frombytes('RGB', (width, height), raw)
        buffer = BytesIO()
        image.save(buffer, format='JPEG', quality=quality, subsampling=0, optimize=True)
        encoded = buffer.getvalue()
        if len(encoded) >= len(stream._data):
            continue
        saved += len(stream._data) - len(encoded)
        replaced += 1
        stream._data = encoded
        stream[NameObject('/Filter')] = NameObject('/DCTDecode')
        stream[NameObject('/ColorSpace')] = NameObject('/DeviceRGB')
        stream.pop(NameObject('/DecodeParms'), None)

with target.open('wb') as handle:
    writer.write(handle)

print('quality', quality, 'replaced', replaced, 'saved image bytes', saved,
      'pdf bytes', target.stat().st_size, 'output', target)
