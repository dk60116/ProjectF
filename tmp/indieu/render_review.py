from pathlib import Path
import sys, json, html
from lxml import etree
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak, KeepTogether

sys.stdout.reconfigure(encoding='utf-8')
root=Path('C:/Git/ProjectF');out=root/'output/indieu'
pdfmetrics.registerFont(TTFont('Malgun','C:/Windows/Fonts/malgun.ttf'))
pdfmetrics.registerFont(TTFont('MalgunBold','C:/Windows/Fonts/malgunbd.ttf'))
ns={'x':'http://www.w3.org/1999/xhtml'}
doc=etree.parse(str(root/'tmp/indieu/filled/index.xhtml'))
tables=doc.findall('.//x:table',ns)
rows=[]
for table in tables:
    rows.append([[''.join(c.itertext()).replace('\r','\n').strip() for c in row] for row in table.findall('./x:tr',ns)])
assert len(rows)==7
content=json.loads((root/'tmp/indieu/content.json').read_text(encoding='utf-8'))
actual='\n'.join(''.join(doc.getroot().itertext()).splitlines())
# Normalize XML line-segmentation whitespace only, keeping source field text.
for value in content.values():
    assert ''.join(value.split()) in ''.join(actual.split()),value[:30]

body=ParagraphStyle('body',fontName='Malgun',fontSize=10.3,leading=16.1,spaceAfter=7,textColor=colors.HexColor('#18242c'),wordWrap='CJK')
small=ParagraphStyle('small',parent=body,fontSize=9,leading=13.3,spaceAfter=3)
label=ParagraphStyle('label',parent=small,fontName='MalgunBold',textColor=colors.HexColor('#294d58'))
head=ParagraphStyle('head',fontName='MalgunBold',fontSize=13,leading=19,spaceBefore=11,spaceAfter=9,textColor=colors.HexColor('#176b64'),keepWithNext=True)
title=ParagraphStyle('title',fontName='MalgunBold',fontSize=21,leading=29,spaceAfter=14,textColor=colors.HexColor('#163c46'))
deck=ParagraphStyle('deck',parent=body,fontSize=10,leading=15,textColor=colors.HexColor('#53636e'))

def p(text,style=body):return Paragraph(html.escape(text).replace('\n','<br/>'),style)
def section(name,paras):
    return [p(name,head)]+[p(t) for t in paras]
def info(data):
    t=Table([[p(k,label),p(v,small)] for k,v in data],colWidths=[106,405],hAlign='LEFT')
    t.setStyle(TableStyle([('BACKGROUND',(0,0),(0,-1),colors.HexColor('#ecf3f2')),('GRID',(0,0),(-1,-1),.4,colors.HexColor('#cbd5d7')),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),8),('RIGHTPADDING',(0,0),(-1,-1),8),('TOPPADDING',(0,0),(-1,-1),6),('BOTTOMPADDING',(0,0),(-1,-1),6)]))
    return t

story=[p('Project F · 참가 신청서',title),p('2026년 충청권 인디게임 공모전 인디유(indie·U) / 일반부',deck),p('HWP 작성본의 내용 검토용이야. 확인 필요 항목과 계획(안)을 확정한 뒤 제출본으로 정리해야 해.',small),Spacer(1,8)]
story += [info([('게임명',rows[1][0][1]),('참가팀명',rows[1][1][1]),('개발 시작',rows[2][1][0]),('팀 소개',rows[2][3][0]),('대표자 / 출신 지역','한택근 / 대전 (원본 기재 기준)'),('역할 / 주요 경력','대표 / 게임회사 프로그래머 2년 재직 경력 (원본 기재 기준)')])]
story += section('가산점 교육 이력', ['원본의 교육 항목은 모두 미선택 상태야. 수료 여부를 확인할 수 없어 체크하지 않았어. 해당 시 수료증 또는 참여 증빙을 첨부해야 해.'])
education=[r[-1] for r in rows[3] if any('게임' in c for c in r)]
story += [p('대전: ’24·’25·’26 인디(inD) 게임스쿨, ’25 인디(inD) 게임어스\n충남: ’24·’25 게임·메타버스 캠퍼스, ’26 게임·메타버스 키움 아카데미\n충북: ’24·’25 게임 아카데미, ’26 내 게임 내가 만드는, 게임학교',small)]
story += section('공모전 지원동기',[content['357']])
story += [PageBreak(),p('게임 정보와 플레이 경험',title)]
story += [info([('게임 장르','생존, 자동화 크래프팅'),('개발기간 / 진척도','2026년 3월~9월 / 30% (원본 기재 기준)'),('개발엔진','Unity 6'),('게임등급',content['428']),('출시 플랫폼','PC·콘솔 선택 유지 / 현재 제출 빌드는 PC, 콘솔은 향후 검토'),('참가 게임 성과',content['447']),('출시 예정일',content['457']+'\n'+content['461']),('국가 / 언어',content['471'])])]
story += section('게임 소개',[content[str(i)] for i in [491,495,499]])
story += [PageBreak(),p('개발 완성도와 경쟁력',title)]
story += section('개발 현황 및 완성도',[content[str(i)] for i in [509,513,517]])
story += section('디자인 및 콘텐츠 구성',[content[str(i)] for i in [527,531,535]])
story += section('차별성 및 경쟁력',[content[str(i)] for i in [545,549]])
story += [PageBreak(),p('사업화와 지속 개발 계획',title)]
story += section('사업화 및 시장진입 계획',[content[str(i)] for i in [559,563,567]])
story += section('향후 개발 및 운영계획',[content[str(i)] for i in [577,581]])
story += section('기타 사항',[content['590']])
story += section('공고문 기준 제출 확인', ['접수 마감: 2026년 9월 29일 18:00 / 온라인 접수', '참가 신청서, 개인정보 동의서, 주민등록 등본, 실행 빌드, 25MB 이하 MP4 플레이 영상, 참가기준 체크리스트가 필요해. 실행 빌드에는 설치방법·조작법·테스트 계정 필요 여부·권장 사양을 함께 준비해야 해. 교육 수료증은 해당자만 제출해.'])

def footer(c,d):
    c.saveState();c.setStrokeColor(colors.HexColor('#ccd6d8'));c.line(42,40,A4[0]-42,40)
    c.setFont('Malgun',8);c.setFillColor(colors.HexColor('#687781'))
    c.drawString(42,27,'Project F · HWP 내용 검토용 · 미확인 항목 확정 전')
    c.drawRightString(A4[0]-42,27,str(d.page));c.restoreState()
path=out/'ProjectF_인디유_신청서_내용검토본.pdf'
SimpleDocTemplate(str(path),pagesize=A4,rightMargin=42,leftMargin=42,topMargin=36,bottomMargin=54,title='Project F 인디유 참가 신청서 내용 검토본',author='Dummy Games').build(story,onFirstPage=footer,onLaterPages=footer)
print(path)

notes='''# 인디유 참가 신청서 작성 메모

원본 HWP는 수정하지 않았어. 작성본은 원본 표·라벨·로고·기존 입력값을 유지하고 빈 서술 항목을 채운 별도 파일이야. 출시일에는 확인 표시를 추가했고, 게임등급은 공식 등급으로 오인되지 않도록 목표 등급으로 표시했어. PDF는 내용 검토용 재배치본으로, 공식 HWP의 인쇄 레이아웃과 동일하지 않아.

## 제출 전에 확정할 정보

- **출시·매출 이력과 일정:** 원본의 Steam 얼리액세스 2026년 4월, 정식 출시 2026년 10월 표기는 현재 날짜·진척도와 대조가 필요해. 임의로 2027년 등으로 바꾸지 않았어. 공고문 2쪽은 9월 29일 기준 정식 출시·얼리액세스 공개 이력 없음 및 매출 미발생을 요구해. 실제 이력이 있으면 참가 자격부터 확인해야 해. 데모·체험판·비공개 및 한시적 테스트는 별도 기준이 있어.
- **거주·팀 구성:** 원본의 ‘대전’은 출신 지역이므로 현재 거주 증명이 아니야. 현재 충청권 거주와 한택근 1인 참가 여부를 확인하고 등본을 준비해야 해.
- **개발 시작 시기:** 빈 결성시기에는 원본 개발기간을 근거로 2026년 3월을 넣었어. 프로젝트 착수일과 ‘인디게임 개발을 시작한 시기’가 다르면 수정해야 해.
- **성과·교육:** 수상, 전시, 이용자 테스트 성과 및 충청권 게임교육 수료 이력은 확인 필요로 남겼어. 확인되지 않은 성과나 가산점은 만들지 않았어. 추가 팀원이 없으면 남는 팀원 행은 사용하지 않으면 돼.
- **사업화 계획:** Steam 유료 패키지, 영어권 진출, 개발 일정, 홍보·업데이트 계획은 프로젝트에 맞춘 제안이야. ‘계획(안)’을 검토해 확정해야 해. 콘솔은 원본 체크를 보존했지만 PC 이후 검토 대상으로 설명했어.
- **권리와 실행 검증:** 외부 아트·사운드 에셋의 이용 권한, 제출 빌드의 실행·조작·저장 동작은 이번 문서 작업에서 검증하지 않았어.

## 공고문 기준 제출물

마감은 2026년 9월 29일 18시야. 공고문 3쪽의 온라인 폼으로 제출하며 마감 후 제출·수정이 불가해.

1. 일반부 참가 신청서: 지정 양식 HWP 및 공고에서 요구하는 PDF 형태를 접수폼에서 확인해 준비해.
2. 개인정보 수집·이용·제공 동의서: 참여 인원 전원 자필 서명이 포함된 하나의 스캔 PDF야.
3. 주민등록 등본: 일반부 참여 인원 전원 기준으로 준비해.
4. 게임 실행 빌드: PC 응용프로그램과 설치방법, 조작법, 필요한 경우 테스트 계정, 권장 사양을 포함해.
5. 플레이 영상: MP4, 25MB 이하, 파일명은 ‘팀명-대표자명’이야.
6. 공모전 참여 기준 체크리스트: 지정 양식 작성 및 서명·날인한 스캔 PDF야.
7. 교육 수료증: 가산점 해당자만 준비해.
8. 인게임 이미지 등 추가 설명자료: 선택 사항이야.

## 작성 근거 및 검증

- 붙임1 공고문 전체 7쪽: 지원자격, 제출목록, 일반부 평가표, 일정 확인.
- 붙임2 원본에 입력된 게임명, 팀명, 대표자, 지역, 경력, 개발기간, 30% 진척도 및 목표 플랫폼.
- 저장소의 PlayerCamera, PlayerController, 설치물·컨베이어·철도·농업·동물·저장 관련 코드, Build_PC 실행파일 존재, 기능별 하네스 README 및 기존 기술소개서.
- 기능 코드를 확인한 것이며 게임 실행이나 하네스 실행 결과를 새로 얻은 것은 아니야. 시장 규모·판매량·성능 수치는 기재하지 않았어.
- 작성된 HWP를 다시 열어 OLE 스트림과 HWP 레코드 구조를 검사하고, 입력한 25개 문단과 원본의 다른 스트림 보존을 확인했어. 독립 HWP 파서로 본문을 재추출해 입력 텍스트를 대조했어.
- PC 화면 조작이나 한글 앱 실행은 하지 않았어. HWP의 최종 페이지 나눔은 한글/호환 편집기에서 열어 확인해야 해. 검토용 PDF는 별도로 렌더링해 확인했어.
'''
(out/'제출전_확인사항.md').write_text(notes,encoding='utf-8')
