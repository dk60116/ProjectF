from pathlib import Path
import sys, struct, zlib, json, hashlib, math
import olefile
from PIL import ImageFont

sys.stdout.reconfigure(encoding='utf-8')
ROOT = Path('C:/Git/ProjectF')
SRC = Path('C:/Users/dk601/Downloads/붙임2.-참가-신청서-외-신청서류/붙임2. 참가 신청서(일반부).hwp')
OUT = ROOT/'output/indieu'
OUT.mkdir(parents=True, exist_ok=True)

# Record IDs refer to the supplied template, never to a regenerated file.
TEXT = {
88: '2026년 3월 개발 시작 (원본에 기재된 개발기간 기준, 최종 확인 필요)',
98: 'Dummy Games는 한택근이 개발하는 1인 인디게임 프로젝트 팀이다. 게임회사 프로그래머 2년 경력을 바탕으로 Unity 기반 게임플레이, 생산·물류 시스템 및 개발 도구를 구축하고 있다.',
357: '자원 채집과 농업·동물 관리가 생산·물류 자동화로 이어지는 재미를 실제 이용자의 관점에서 검증하고자 지원한다. 예선 심사와 멘토링을 통해 초반 목표 안내, 제작·설치 조작, 자동화 성취감에 대한 피드백을 받고, 본선까지 핵심 플레이 흐름과 빌드 안정성을 개선하고자 한다. 이를 바탕으로 Steam 출시를 위한 콘텐츠 범위와 사업화 계획을 구체화할 계획이다.',
428: '전체~12세 이용가 목표 (등급 미확정)',
457: 'Steam 얼리액세스: [일정 확인 필요] 원본 2026년 4월 표기',
461: 'Steam 정식 출시: [일정 확인 필요] 원본 2026년 10월 표기',
471: '한국 및 영어권 시장 목표(안) / 한국어·영어 지원 예정',
491: 'Project F는 자연환경을 탐색하며 자원을 채집하고, 농업과 동물 관리에서 공장 생산·물류망 구축으로 활동 범위를 넓혀가는 3D 생존·자동화 크래프팅 게임이다. 현재 PC 빌드를 중심으로 개발하고 있으며, 플레이어가 직접 움직이고 설비를 배치해 자신만의 생산 거점을 만든다.',
495: '플레이는 자원 확보 → 도구·설비 제작 → 생산과 운송 연결 → 병목 개선 → 새로운 지역 확장으로 이어진다. 키보드 이동과 마우스 선택·설치로 직접 작업하던 과정을 컨베이어, 로봇팔, 파이프, 철도로 자동화한다. 생산물이 이동하는 모습을 보며 공급 부족과 운송 지연을 해결하는 과정이 핵심 재미다.',
499: '주요 대상은 공장 설계와 효율 개선을 즐기는 자동화 게임 이용자, 채집·농업·제작을 함께 즐기는 샌드박스 이용자다. 초기에는 가까운 자원과 소규모 생산을 익히고, 이후 유체·전력·장거리 운송을 결합하는 단계적 난이도 구성을 목표로 한다.',
509: '원본 신청서 기준 개발진척도는 30%이며 핵심 시스템을 확장·통합하는 단계다. 코드와 데이터에서 자원 채집·제작, 설치물 배치, 컨베이어·분배기·로봇팔, 파이프·탱크·펌프, 생산 설비, 철도·열차 자동운행, 농업·파종·관수, 동물 먹이·성장 관리, 월드 저장·복원 기능을 확인할 수 있다.',
513: 'Windows 실행 빌드가 있으며, 컨베이어 운송·유체량 보존·열차 운행·저장 복원 등 주요 기능을 반복 검증하는 테스트 하네스를 구축했다. 다만 최신 제출 빌드의 전체 진행과 장시간 안정성 검증은 별도로 필요하다. 제출 전 신규 시작부터 생산·운송 연결, 저장 후 재진입까지의 대표 시나리오를 점검할 계획이다.',
517: '정식 출시 전에는 튜토리얼과 단계별 목표, 제작 비용·생산 속도 균형, UI 가독성, 사운드와 시각 표현의 일관성을 보완한다. 대규모 설비와 장거리 운송에서 발생할 수 있는 성능 저하 및 저장·복원 오류를 우선 개선한다.',
527: '캐릭터와 생산 설비를 함께 내려다보는 사선 시점의 3D 화면을 사용한다. 자연 지형·수목·동물과 산업 설비가 한 공간에 배치되며, 컨베이어 위의 아이템과 움직이는 열차가 생산 흐름을 시각적으로 보여준다. 화면 확대·축소로 주변 작업과 넓은 배치를 살펴볼 수 있다.',
531: '인벤토리·제작 목록·설비 정보·지도 UI로 자원 확보와 설비 운영에 필요한 정보를 제공한다. 밭과 파종·관수 설비, 동물 먹이 공급, 공장과 철도망을 연결하여 자연 속 거점이 생산 공간으로 확장되는 과정을 콘텐츠의 중심에 둔다.',
535: '후속 작업은 설비별 식별성, 상태 표시와 상호작용 피드백, UI 표현의 통일이다. 효과음·배경음의 구성과 음량 균형도 플레이 흐름에 맞춰 보완할 계획이다. 실제 제출 이미지와 영상은 최신 게임 빌드에서 별도로 준비한다.',
545: '차별화 방향은 농업·동물 관리와 산업 자동화를 분리된 활동으로 두지 않고 하나의 생산 생활권으로 연결하는 데 있다. 파종·관수와 먹이 공급을 설비·운송망의 설계 문제로 확장하고, 가까운 작업은 직접 수행하며 반복 작업은 점차 자동화하는 성장 경험을 지향한다.',
549: '컨베이어의 근거리 운송, 파이프의 유체 전달, 철도의 거점 간 운송을 함께 설계하면서 서로 다른 물류 수단을 조합할 수 있다. 화면 밖 생산과 월드 저장을 위한 상태 관리 및 성능 최적화 기반을 구축해 공장 확장을 뒷받침한다. 실제 경쟁력은 이용자 테스트로 검증하고, 성능 수치나 완성도를 과장하지 않는다.',
559: '[사업화 계획(안)] PC 자동화·크래프팅 이용자를 우선 대상으로 삼고, 한국어·영어를 지원하는 Steam 유료 패키지 판매를 기본 방향으로 검토한다. 초기 범위를 핵심 생산·물류 경험에 집중하고, 콘솔 대응은 PC 버전의 조작 체계와 안정성을 확보한 뒤 검토한다.',
563: '출시 일정은 현재 진척도와 테스트 결과를 바탕으로 재산정한다. 원본의 얼리액세스 2026년 4월·정식 출시 2026년 10월 표기는 확인이 필요하다. 공고 마감일 기준 정식 출시·얼리액세스 공개 및 매출 발생 이력을 먼저 확인하고, 자격 충족 시 공모전 이후의 출시 계획을 확정한다.',
567: '홍보는 직접 채집하던 작업이 자동 생산망으로 바뀌는 과정을 보여주는 짧은 영상과 데모를 중심으로 준비한다. 테스트 참가자의 이탈 구간·조작 불편·자동화 이해도를 수집해 개선하고, 출시 준비 단계에서 상점 페이지와 커뮤니티를 운영할 계획이다. 판매가격·출시일·퍼블리싱 여부는 검증 후 결정한다.',
577: '[개발 일정(안)] 9월 접수 전에는 제출 빌드·설치 및 조작 안내·대표 플레이 영상 준비에 집중한다. 예선 통과 시 10월 멘토링에서 초반 안내와 핵심 재미를 검증하고, 11월 본선까지 오류 수정과 UI·밸런스 개선 결과를 반영한다. 이후 테스트 결과에 따라 콘텐츠 범위와 출시 일정을 확정한다.',
581: '대표자가 기획·프로그래밍·통합·검증을 담당하며, 기능별 테스트 하네스로 반복 작업을 줄이고 핵심 시스템을 지속 개선한다. 출시 전에는 신규 제작 콘텐츠, 성능, 저장 안정성을 순차 검증하고, 출시 후에는 이용자 제보를 바탕으로 오류 수정·밸런스 조정·생산 및 물류 콘텐츠 업데이트를 추진할 계획이다.',
}
INSERT = {
447: '[확인 필요] 수상·전시·판매·이용자 테스트 실적은 확인 후 기재. 현재 자료로는 정량 성과를 확정할 수 없음.',
590: '[제출 전 확인] 현재 충청권 거주 및 등본, 출시·매출 이력, 교육 수료 가산점, 외부 에셋 이용 권한을 확인해야 한다. 기존 콘솔 표시는 향후 목표로 해석했으며 현재 제출 빌드는 PC 기준이다. 본 문서의 계획(안)은 개발자 확인 후 확정한다.',
}

def records(data):
    out=[]; pos=0
    while pos<len(data):
        h=struct.unpack_from('<I',data,pos)[0];pos+=4
        tag=h&1023;level=(h>>10)&1023;size=h>>20
        if size==4095:size=struct.unpack_from('<I',data,pos)[0];pos+=4
        out.append([tag,level,bytearray(data[pos:pos+size])]);pos+=size
    assert pos==len(data)
    return out

ole=olefile.OleFileIO(SRC)
rs=records(zlib.decompress(ole.openstream('BodyText/Section0').read(),-15))
assert rs[88][2].decode('utf-16le').startswith('※ 개인')
assert rs[491][2].decode('utf-16le').startswith('※ 게임의 장르')
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',110)

def line_starts(text,width):
    # Use the same installed font and conservative line width as the form.
    start=0; result=[0]
    for i in range(1,len(text)):
        if font.getlength(text[start:i+1])*10 > width:
            cut=text.rfind(' ',start+1,i)
            cut=cut+1 if cut>start else i
            if cut<=start:cut=i
            result.append(cut);start=cut
    return sorted(set(result))

inserts={}; changed_headers=set()
for key,value in TEXT.items():
    assert rs[key][0]==67
    rs[key][2]=bytearray((value+'\r').encode('utf-16le'))
    changed_headers.add(key-1)
for key,value in INSERT.items():
    assert rs[key][0]==66 and rs[key+1][0]==68
    inserts[key]=[67,rs[key][1]+1,bytearray((value+'\r').encode('utf-16le'))]
    changed_headers.add(key)

# Each cell owns its paragraphs; preserve all labels, borders and original fields.
for ci,(tag,lev,cell) in enumerate(rs):
    if tag!=72 or len(cell)!=46:continue
    end=ci+1
    while end<len(rs) and not (rs[end][1]<=lev and rs[end][0]!=66):end+=1
    headers=[i for i in range(ci+1,end) if rs[i][0]==66 and rs[i][1]==lev]
    if not any(i in changed_headers for i in headers):continue
    width=struct.unpack_from('<i',cell,16)[0]
    pads=struct.unpack_from('<4H',cell,24)
    usable=width-pads[0]-pads[1]-220
    y=0
    # All paragraphs in an edited text cell use consistent 11 pt black body text.
    for hi in headers:
        stop=next((i for i in range(hi+1,end) if rs[i][0]==66),end)
        tr=inserts.get(hi) or next((rs[i] for i in range(hi+1,stop) if rs[i][0]==67),None)
        if tr is None:continue
        text=tr[2].decode('utf-16le').rstrip('\r')
        starts=line_starts(text,usable)
        h=rs[hi][2]
        original=struct.unpack_from('<I',h,0)[0]
        struct.pack_into('<I',h,0,(original&0x80000000)+len(tr[2])//2)
        struct.pack_into('<H',h,8,16) # existing left-aligned paragraph style
        struct.pack_into('<H',h,12,1)
        struct.pack_into('<H',h,16,len(starts))
        for i in range(hi+1,stop):
            if rs[i][0]==68:rs[i][2]=bytearray(struct.pack('<II',0,18))
            elif rs[i][0]==69:
                segs=[]
                for start in starts:
                    segs.append(struct.pack('<8iI',start,y,1100,1100,935,660,0,usable,0x60000))
                    y+=1760
                rs[i][2]=bytearray(b''.join(segs))
    oldheight=struct.unpack_from('<i',cell,20)[0]
    struct.pack_into('<i',cell,20,max(oldheight,y+pads[2]+pads[3]+200))
    flags=struct.unpack_from('<I',cell,4)[0]
    struct.pack_into('<I',cell,4,flags & ~(3<<5)) # top alignment

# Refresh each edited table's overall height from the actual row heights.
for ti,(tag,level,data) in enumerate(rs):
    if tag!=71 or data[:4]!=b' lbt':continue
    end=next((i for i in range(ti+1,len(rs)) if rs[i][1]<=level),len(rs))
    heights={}
    for i in range(ti+1,end):
        t,l,d=rs[i]
        if t==72 and len(d)==46:
            row=struct.unpack_from('<H',d,10)[0]
            heights[row]=max(heights.get(row,0),struct.unpack_from('<i',d,20)[0])
        if t==77:
            flags=struct.unpack_from('<I',d,0)[0]
            struct.pack_into('<I',d,0,(flags&~3)|2) # permit page splits
    if heights:struct.pack_into('<I',data,20,sum(heights.values()))

def encode(recs):
    out=[]
    for tag,level,data in recs:
        n=len(data)
        out.append(struct.pack('<I',tag|(level<<10)|(min(n,4095)<<20)))
        if n>=4095:out.append(struct.pack('<I',n))
        out.append(data)
    return b''.join(out)

new=[]
for i,r in enumerate(rs):
    new.append(r)
    if i in inserts:new.append(inserts[i])
raw=encode(new)
compress=zlib.compressobj(9,zlib.DEFLATED,-15)
stream=compress.compress(raw)+compress.flush()
assert len(stream)>=4096

# Replace only the section stream in the original CFB container. Reuse the
# original FAT and directory tree; do not touch images, styles or metadata.
file=bytearray(SRC.read_bytes()); sector=ole.sectorsize
fat=list(ole.fat)
fat.extend([0xffffffff]*(128-len(fat)))
old=ole.direntries[5].isectStart
while old<0xfffffffa:
    nxt=fat[old];fat[old]=0xffffffff
    file[(old+1)*sector:(old+2)*sector]=b'\0'*sector
    old=nxt
start=len(file)//sector-1;count=math.ceil(len(stream)/sector)
assert start+count<=128 and ole.num_fat_sectors==1
for i in range(count):fat[start+i]=start+i+1 if i<count-1 else 0xfffffffe
file.extend(stream.ljust(count*sector,b'\0'))
fatsector=struct.unpack_from('<I',file,76)[0]
file[(fatsector+1)*sector:(fatsector+2)*sector]=struct.pack('<128I',*fat[:128])
directory_sector=ole.first_dir_sector
offset=5*128
while offset>=sector:directory_sector=fat[directory_sector];offset-=sector
entry=(directory_sector+1)*sector+offset
struct.pack_into('<I',file,entry+116,start)
struct.pack_into('<Q',file,entry+120,len(stream))
dst=OUT/'ProjectF_인디유_참가신청서_작성본.hwp'
dst.write_bytes(file)

# Independent OLE reopen, byte-for-byte preservation, record and field checks.
check=olefile.OleFileIO(dst)
for name in ole.listdir():
    if name!=['BodyText','Section0']:
        assert ole.openstream(name).read()==check.openstream(name).read(),name
actual=zlib.decompress(check.openstream('BodyText/Section0').read(),-15)
assert actual==raw
decoded='\n'.join(r[2].decode('utf-16le') for r in records(actual) if r[0]==67)
for text in list(TEXT.values())+list(INSERT.values()):assert text in decoded
assert '※ 게임의 장르' not in decoded
(ROOT/'tmp/indieu/filled_text.txt').write_text(decoded,encoding='utf-8')
(ROOT/'tmp/indieu/content.json').write_text(json.dumps({**TEXT,**INSERT},ensure_ascii=False,indent=2),encoding='utf-8')
print('Written:',dst,'bytes:',len(file),'section:',len(stream),'updated fields:',len(TEXT)+len(INSERT))
print('Original SHA256:',hashlib.sha256(SRC.read_bytes()).hexdigest())

# Convert the actual saved HWP, independently of the authoring records.
# pyhwp cannot parse this template's summary property stream, so omit only
# summary metadata from this review conversion; the HWP retains it unchanged.
from hwp5 import xmlmodel
from hwp5.hwp5html import HTMLTransform
from hwp5.xmlmodel import wrap_modelevents,give_elements_unique_id,HwpDoc
from itertools import chain
def events(self,**kwargs):
    if kwargs.get('embedbin') and 'BinData' in self:kwargs['embedbin']=self['BinData']
    else:kwargs.pop('embedbin',None)
    ev=chain(self.docinfo.events(**kwargs),self.text.events(**kwargs))
    return give_elements_unique_id(wrap_modelevents((HwpDoc,dict(version=self.header.version),{}),ev))
xmlmodel.Hwp5File.events=events
(ROOT/'tmp/indieu/filled').mkdir(parents=True,exist_ok=True)
HTMLTransform().transform_hwp5_to_dir(xmlmodel.Hwp5File(str(dst)),str(ROOT/'tmp/indieu/filled'))
print('Saved HWP parsed and converted successfully.')
