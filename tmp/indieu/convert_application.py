from pathlib import Path
from itertools import chain

from hwp5 import xmlmodel
from hwp5.hwp5html import HTMLTransform
from hwp5.xmlmodel import HwpDoc, give_elements_unique_id, wrap_modelevents

src = Path(r'C:\Users\dk601\Downloads\붙임2.-참가-신청서-외-신청서류\붙임2. 참가 신청서(일반부).hwp')
dst = Path(r'C:\Git\ProjectF\tmp\indieu\application_html')
dst.mkdir(parents=True, exist_ok=True)


def events_without_broken_summary(self, **kwargs):
    if kwargs.get('embedbin') and 'BinData' in self:
        kwargs['embedbin'] = self['BinData']
    else:
        kwargs.pop('embedbin', None)
    content = chain(self.docinfo.events(**kwargs), self.text.events(**kwargs))
    return give_elements_unique_id(
        wrap_modelevents((HwpDoc, dict(version=self.header.version), {}), content)
    )


xmlmodel.Hwp5File.events = events_without_broken_summary
HTMLTransform().transform_hwp5_to_dir(xmlmodel.Hwp5File(str(src)), str(dst))
print(dst)
