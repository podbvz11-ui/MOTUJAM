"""ChatGPT 로 만든 원본 이미지를 게임용으로 다듬는다: 투명 여백을 잘라내고 알맞은 크기로 줄인다.
    python skin.py <원본 폴더> <출력 폴더(StreamingAssets/Skin)>
콤보 숫자는 digit_0.png ~ digit_9.png 로 나눈다."""
import sys, os, subprocess
import numpy as np
import img

src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)

def bbox(a, thr=8):
    ys, xs = np.nonzero(a[..., 3] > thr)
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1

def export(name, a, box, size, out=None):
    """box=(x0,y0,x1,y1) 를 잘라 size=(w,h) 로 줄여 저장한다. size 의 한쪽이 None 이면 비율을 지킨다."""
    x0, y0, x1, y1 = box
    c = a[y0:y1, x0:x1]
    w, h = size
    if w is None: w = max(1, round(h * c.shape[1] / c.shape[0]))
    if h is None: h = max(1, round(w * c.shape[0] / c.shape[1]))
    hh, ww = c.shape[:2]
    subprocess.run([img.FF, '-hide_banner', '-loglevel', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgba', '-s', '%dx%d' % (ww, hh), '-i', '-',
                    '-vf', 'premultiply=inplace=1,scale=%d:%d:flags=lanczos,unpremultiply=inplace=1' % (w, h), '-frames:v', '1', '-update', '1',
                    os.path.join(dst, out or name + '.png')], input=np.ascontiguousarray(c).tobytes(), check=True)
    return w, h

def pad(box, a, p):
    h, w = a.shape[:2]
    return max(0, box[0] - p), max(0, box[1] - p), min(w, box[2] + p), min(h, box[3] + p)

L = {f[:-4]: img.load(os.path.join(src, f)) for f in sorted(os.listdir(src)) if f.endswith('.png')}
report = {}

# 배경: 16:9 로 가운데를 자른다
for n in ('bg_title', 'bg_select', 'bg_play', 'bg_result'):
    if n in L:
        a = L[n]; h, w = a.shape[:2]; ch = round(w * 9 / 16); y = (h - ch) // 2
        report[n] = export(n, a, (0, y, w, y + ch), (1920, 1080))

# 내용에 맞춰 자르고 높이(또는 폭)를 정해 줄이는 것들: 이름 -> (폭, 높이)
FIT = {
    'note_white': (342, None), 'note_blue': (342, None), 'judge_line': (992, None),
    'key_white': (220, None), 'key_white_pressed': (220, None), 'key_blue': (220, None), 'key_blue_pressed': (220, None),
    'key_beam': (186, None), 'hit_effect': (384, None), 'spark': (64, None),
    'combo_label': (None, 80), 'full_combo': (None, 150), 'logo': (1100, None),
    'title_select': (None, 120), 'title_result': (None, 120),
    'badge_easy': (None, 96), 'badge_normal': (None, 96), 'badge_hard': (None, 96), 'badge_veryhard': (None, 96), 'badge_extreme': (None, 96), 'badge_4k': (None, 96), 'badge_6k': (None, 96),
    'arrow': (None, 96), 'row_selected': (1040, None), 'progress_bg': (992, None), 'progress_fill': (992, None),
    'panel_score': (700, None), 'panel_song': (800, None), 'panel_list': (900, None), 'panel_info': (900, None),
    'panel_rank': (700, None), 'panel_result': (1000, None), 'jacket_frame': (640, None),
}
for n, size in FIT.items():
    if n in L: report[n] = export(n, L[n], pad(bbox(L[n], 2), L[n], 6), size)   # 옅은 빛 번짐이 뚝 끊기지 않게 여유를 둔다

# 캐릭터
for n in ('char_title', 'char_result_good', 'char_result_bad'):
    if n in L: report[n] = export(n, L[n], pad(bbox(L[n], 2), L[n], 6), (640, None))

# 같은 묶음은 같은 영역으로 잘라 서로의 크기 비율을 지킨다
def group(names, size, axis):
    have = [n for n in names if n in L]
    if not have: return
    bs = [bbox(L[n]) for n in have]
    for n, b in zip(have, bs):
        if axis == 'y': b = (b[0], min(x[1] for x in bs), b[2], max(x[3] for x in bs))
        else: b = (min(x[0] for x in bs), min(x[1] for x in bs), max(x[2] for x in bs), max(x[3] for x in bs))
        report[n] = export(n, L[n], b, size)
group(['judge_perfect', 'judge_great', 'judge_good', 'judge_miss'], (None, 180), 'y')
group(['rank_s', 'rank_a', 'rank_b', 'rank_c', 'rank_d'], (None, 512), 'xy')

# 세로로 늘어나는 것들
for n in ('long_body_white', 'long_body_blue'):
    if n in L:
        b = bbox(L[n]); report[n] = export(n, L[n], (b[0], 256, b[2], 1280), (128, 256))
if 'track_bg' in L: report['track_bg'] = export('track_bg', L['track_bg'], (0, 0) + L['track_bg'].shape[1::-1], (256, 540))
if 'track_side' in L:
    b = bbox(L['track_side'], 24); report['track_side'] = export('track_side', L['track_side'], (b[0], 0, b[2], L['track_side'].shape[0]), (None, 1080))
for n in ('jacket_default', 'icon'):
    if n in L: report[n] = export(n, L[n], (0, 0) + L[n].shape[1::-1], (512, 512))

# 콤보 숫자: 두 줄(0~4, 5~9)을 글자 하나씩으로 나눈다
if 'combo_digits' in L:
    a = L['combo_digits']; al = a[..., 3] > 24
    def runs(mask, min_gap):
        e = np.flatnonzero(np.diff(np.concatenate([[0], mask.astype(np.int8), [0]])))
        r = [[int(s), int(t)] for s, t in zip(e[::2], e[1::2])]
        out = []
        for s, t in r:
            if out and s - out[-1][1] < min_gap: out[-1][1] = t
            else: out.append([s, t])
        return out
    rows = runs(al.any(1), 40)
    boxes = []
    for y0, y1 in rows:
        for x0, x1 in runs(al[y0:y1].any(0), 30): boxes.append((x0, y0, x1, y1))
    print('숫자 조각:', len(boxes), '줄:', len(rows))
    if len(boxes) == 10:
        cw = max(b[2] - b[0] for b in boxes) + 16; ch = max(b[3] - b[1] for b in boxes) + 16
        for i, b in enumerate(boxes):
            cx, cy = (b[0] + b[2]) // 2, (b[1] + b[3]) // 2
            cell = np.zeros((ch, cw, 4), np.uint8)
            x0, y0 = cx - cw // 2, cy - ch // 2
            sx0, sy0 = max(0, x0), max(0, y0); sx1, sy1 = min(a.shape[1], x0 + cw), min(a.shape[0], y0 + ch)
            cell[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = a[sy0:sy1, sx0:sx1]
            report['digit_%d' % i] = export('digit_%d' % i, cell, (0, 0, cw, ch), (None, 200))

# 자켓 액자의 안쪽 구멍 비율 (게임에서 자켓 크기를 맞추는 데 쓴다)
if 'jacket_frame' in L:
    a = L['jacket_frame']; b = bbox(a); mid = a[(b[1] + b[3]) // 2, b[0]:b[2], 3]
    hole = np.flatnonzero(mid < 24)
    print('액자 구멍: 폭 대비 %.3f (왼쪽 테두리 %.3f)' % ((hole.max() - hole.min() + 1) / (b[2] - b[0]), hole.min() / (b[2] - b[0])))
for n in sorted(report): print('%-20s %dx%d' % (n, *report[n]))
print(len(report), '개')
