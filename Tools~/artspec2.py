"""추가 아트 명세서(난이도 배지 2종). python artspec2.py <출력 html>"""
import sys, html
ROWS = [
 ('E-13', 'badge_veryhard.png', '200×64', '가로 1536×1024', '투명',
  '난이도 표시 "VERY HARD". 기존 E-09(badge_hard.png)와 모양·크기·테두리 굵기가 완전히 같은 알약 모양 배지. 바탕은 보라~남보라 그라데이션, 글자는 흰색. '
  '글자가 두 단어라 길므로 배지 길이를 늘리지 말고 글자 크기를 줄여서 한 줄에 넣는다. 양 끝의 작은 화살표 장식도 E-09 와 같게.'),
 ('E-14', 'badge_extreme.png', '200×64', '가로 1536×1024', '투명',
  '난이도 표시 "EXTREME". E-09 와 모양·크기가 완전히 같은 알약 모양 배지. 바탕은 검정에 가까운 어두운 빨강, 테두리는 금색~주황으로 빛나고, '
  '가장 높은 난이도답게 가장자리에 불꽃이나 번개 같은 빛 효과를 약하게 넣는다(배지 밖으로 크게 튀어나오지 않게). 글자는 흰색~연한 금색.'),
]
CSS = """
@page { size: A4; margin: 16mm 14mm; }
body { font-family: 'Malgun Gothic', sans-serif; font-size: 10.5pt; line-height: 1.6; color: #111; margin: 0; }
h1 { font-size: 20pt; margin: 0 0 2mm; } h2 { font-size: 13pt; margin: 7mm 0 2mm; border-bottom: 2px solid #111; padding-bottom: 1mm; }
.sub { color: #444; } li { margin: 0.8mm 0; } ol, ul { padding-left: 5.5mm; margin: 1mm 0; }
.note { background: #eef3fa; border-left: 4px solid #2b6cb0; padding: 2mm 3mm; margin: 3mm 0; }
table { border-collapse: collapse; width: 100%; margin: 2mm 0; } th, td { border: 1px solid #999; padding: 1.5mm 2mm; vertical-align: top; text-align: left; }
th { background: #e6e6e6; font-size: 9pt; } td.id { font-weight: bold; white-space: nowrap; } td.file { font-family: Consolas, monospace; font-size: 8.5pt; white-space: nowrap; } td.s { font-size: 9pt; }
.sw { display: inline-block; width: 9mm; height: 4.5mm; border: 1px solid #555; vertical-align: middle; margin-right: 1.5mm; }
svg { width: 100%; border: 1px solid #333; display: block; margin-top: 2mm; } svg text { font-family: 'Malgun Gothic', sans-serif; font-size: 13px; }
"""
rows = ''.join('<tr><td class="id">%s</td><td class="file">%s</td><td class="s">%s</td><td class="s">%s</td><td class="s">%s</td><td>%s</td></tr>' % tuple(html.escape(c) for c in r) for r in ROWS)
def pill(x, fill, stroke, label, tcol='#fff'):
    return ('<rect x="%d" y="30" width="150" height="46" rx="23" fill="%s" stroke="%s" stroke-width="3"/>'
            '<text x="%d" y="59" text-anchor="middle" fill="%s" font-weight="bold" font-style="italic">%s</text>') % (x, fill, stroke, x + 75, tcol, label)
svg = ('<svg viewBox="0 0 900 110"><rect width="900" height="110" fill="#10142a"/>'
       + pill(20, '#2e8b62', '#5fe0a8', 'EASY') + pill(195, '#2f5fb5', '#7fb0ff', 'NORMAL') + pill(370, '#a8385e', '#ff7fa8', 'HARD')
       + pill(545, '#5b3aa8', '#b08cff', 'VERY HARD') + pill(720, '#3a0a0a', '#ffb347', 'EXTREME', '#ffe9b0')
       + '<text x="620" y="98" text-anchor="middle" fill="#ffd14d">▲ 새로 만들 것 (E-13)</text><text x="795" y="98" text-anchor="middle" fill="#ffd14d">▲ 새로 만들 것 (E-14)</text></svg>')
body = ('<h1>모투잼 아트 제작 명세서 — 추가분</h1>'
        '<p class="sub">난이도 두 단계(Very Hard, Extreme)가 새로 생겨서 필요한 이미지 2장. 앞서 만든 51장과 같은 그림체로 만든다.</p>'
        '<div class="note"><b>ChatGPT 에게.</b> 이 문서는 게임에 쓸 이미지 주문서의 추가분이다. 사용자가 함께 올린 <b>badge_hard.png</b>(기존 HARD 배지)를 기준 그림으로 삼아, '
        '그와 <b>모양·크기·캔버스 안 위치가 완전히 같고</b> 색과 글자만 다른 배지를 아래 표대로 한 장씩 만든다. 아래 공통 규칙을 모두 지킨다.</div>'
        '<h2>사용 방법 (사람용)</h2><ol>'
        '<li>이 PDF 와 함께 <b>SkinSource~ 폴더의 badge_hard.png</b> 를 ChatGPT 에 올린다. 기준 그림이 있어야 기존 배지와 모양이 맞는다.</li>'
        '<li>"이 문서대로 E-13 만들어줘", "E-14 만들어줘"라고 한 장씩 주문한다.</li>'
        '<li>받은 그림을 표의 파일 이름 그대로 저장해서 전달한다.</li></ol>'
        '<h2>1. 공통 규칙</h2><ol>'
        '<li>이미지 한 장에 배지 하나. 다른 물체·워터마크·서명을 넣지 않는다.</li>'
        '<li>배경은 완전히 투명한 PNG. 체크무늬나 흰 바탕을 그려 넣지 않는다.</li>'
        '<li>배지는 캔버스 한가운데에 두고, 빛 번짐까지 캔버스 안에 들어오게 가장자리에 5% 이상 여백을 둔다.</li>'
        '<li>글자는 표에 따옴표로 적힌 영문만 철자 그대로 넣는다. 한글은 넣지 않는다.</li>'
        '<li>정면에서 본 평평한 2D 그림. 기울이거나 3D 로 돌리지 않는다.</li>'
        '<li>글씨체는 기존 배지와 같은 굵고 오른쪽으로 기울어진 산세리프.</li></ol>'
        '<h2>2. 그림체와 색</h2>'
        '<p>어두운 밤 무대 위에서 네온이 빛나는 미래풍 아케이드 리듬 게임 느낌. 깔끔하고 선명한 벡터풍 2D, 가늘고 밝은 네온 테두리, 은은한 빛 번짐.</p><table>'
        '<tr><td style="width:36%"><span class="sw" style="background:#9966FF"></span><b>#9966FF</b> 보라</td><td>Very Hard 의 주 색</td></tr>'
        '<tr><td><span class="sw" style="background:#3A0A0A"></span><b>#3A0A0A</b> 어두운 빨강</td><td>Extreme 의 바탕</td></tr>'
        '<tr><td><span class="sw" style="background:#FFB347"></span><b>#FFB347</b> 금빛 주황</td><td>Extreme 의 테두리와 빛</td></tr></table>'
        '<h2>3. 이미지 목록</h2>'
        '<table><tr><th style="width:7%">번호</th><th style="width:22%">파일 이름</th><th style="width:12%">게임 안 크기</th><th style="width:13%">생성 캔버스</th><th style="width:8%">배경</th><th>그릴 내용</th></tr>'
        + rows + '</table>'
        '<h2>4. 놓이는 자리</h2><p>곡 선택 화면에서 곡 이름 아래, 키 수 배지(4K/6K) 옆에 한 번에 하나만 나온다. 다섯 난이도가 나란히 놓였을 때의 색 구분은 아래와 같다(모양은 예시이며 실제 모양은 badge_hard.png 를 따른다).</p>'
        + svg)
open(sys.argv[1], 'w', encoding='utf-8').write('<!doctype html><html lang="ko"><head><meta charset="utf-8"><title>모투잼 아트 명세서 추가분</title><style>%s</style></head><body>%s</body></html>' % (CSS, body))
