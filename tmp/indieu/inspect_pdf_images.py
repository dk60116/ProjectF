from pathlib import Path
from pypdf import PdfReader

src = Path(r'C:\Users\dk601\Downloads\붙임2.-참가-신청서-외-신청서류\붙임2. 참가 신청서(일반부).pdf')
pdf = PdfReader(src)
seen = set()
for page_number, page in enumerate(pdf.pages, 1):
    resources = page.get('/Resources') or {}
    objects = resources.get('/XObject') or {}
    for name, indirect in objects.items():
        if (indirect.idnum, indirect.generation) in seen:
            continue
        seen.add((indirect.idnum, indirect.generation))
        obj = indirect.get_object()
        if obj.get('/Subtype') != '/Image':
            continue
        print(page_number, name, indirect.idnum,
              obj.get('/Width'), obj.get('/Height'),
              obj.get('/Filter'), obj.get('/ColorSpace'),
              'SMask' if '/SMask' in obj else '',
              len(obj._data), len(obj.get_data()))
print('unique resource objects', len(seen))
