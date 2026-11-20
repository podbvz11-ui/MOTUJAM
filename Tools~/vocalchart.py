"""
보컬(목소리)에 맞춘 채보를 만든다. 목소리가 없는 구간은 반주의 음(멜로디/베이스)에 맞춘다.
박자 격자에 맞추지 않고, 실제로 소리가 나는 시각에 노트를 둔다.

사용법 (separate.py 로 stems 를 먼저 만들어야 한다):
    python vocalchart.py <Songs 폴더> <stems 폴더> [곡 이름 ...]

각 곡의 song.json 에서 제목/음원/자켓/BPM 은 그대로 두고 charts 만 새로 쓴다.
"""
import sys, os, json, zlib, random
import numpy as np
import autochart as A
from autochart import SR, HOP, FPS

DIFFS = {
    #          노트 사이 최소 간격(초): 보컬 / 반주,  음절 점수 하한,  동시치기 비율, 같은 레인 연타를 피하는 간격(초)
    'Easy':   dict(gap=0.36, inst_gap=0.50, floor=1.6, chord=0.00, jack=0.45),
    'Normal': dict(gap=0.21, inst_gap=0.30, floor=0.9, chord=0.06, jack=0.30),
    'Hard':   dict(gap=0.115, inst_gap=0.17, floor=0.45, chord=0.14, jack=0.17),
}
MERGE = 0.09        # 이 시간(초) 안에 붙은 후보는 한 음절(자음+모음)로 본다
LONG_MIN = 0.55     # 이 시간(초) 이상 한 음을 끌면 롱노트
LONG_MAX = 4.0
INST_GAP_MIN = 1.2  # 목소리가 이 시간 이상 쉬는 구간만 반주에 맞춘다


def frame_of(t):
    return int(round((t + A.BIAS) * SR / HOP - A.N / 2 / HOP))


def pitch_at(x, t):
    """t 직후 구간의 기본 주파수(Hz). 못 찾으면 0."""
    s = int((t + 0.03) * SR)
    seg = x[s:s + 2560].astype(np.float64)
    if len(seg) < 2560 or np.abs(seg).max() < 1e-4:
        return 0.0
    seg = seg - seg.mean()
    f = np.fft.rfft(seg * np.hanning(len(seg)), 8192)
    ac = np.fft.irfft(np.abs(f) ** 2)
    lo, hi = int(SR / 900), int(SR / 80)
    k = lo + int(np.argmax(ac[lo:hi]))
    if ac[k] < 0.3 * ac[0]:
        return 0.0
    return SR / k


class Track:
    """곡 하나의 분석 결과: 보컬 음절 시작점들과 반주 음 시작점들."""

    def __init__(self, vocals, inst):
        self.dur = len(vocals) / SR
        env_v, _, rms_v, _ = A.features(vocals)
        env_i, _, rms_i, cen_i = A.features(inst)
        n = len(env_v)

        # 목소리가 나오는 구간
        rms_s = A.moving_mean(rms_v, FPS * 0.06)
        ref = np.percentile(rms_s, 95)
        active = rms_s > 0.08 * ref
        self.vocal_ratio = float(active.mean())

        # ---- 보컬 음절 시작점 ----
        pf, pw = A.pick_peaks(env_v, 0.45)
        look = int(0.09 * FPS)
        keep = np.array([active[int(f):int(f) + look].any() for f in pf], dtype=bool)
        pf, pw = pf[keep], np.minimum(pw[keep], 8.0)

        # 진짜 음절 시작은 소리가 잠깐 작아졌다가 다시 커진다 (한 음을 끄는 중의 떨림과 구분)
        rs = A.moving_mean(rms_v, FPS * 0.02)
        b = int(0.08 * FPS)
        dip = np.array([1 - rs[max(0, f - b):f + 1].min() / (rs[f:f + b].max() + 1e-9) for f in pf.astype(int)])
        score = pw * (0.35 + np.clip(dip, 0, 1))
        t_all = A.frame_time(pf)

        # 자음과 모음처럼 바로 붙은 후보는 하나로 합친다 (가장 센 쪽의 시각을 쓴다)
        vt, vw = [], []
        k = 0
        while k < len(t_all):
            j = k
            while j + 1 < len(t_all) and t_all[j + 1] - t_all[k] < MERGE: j += 1
            m = k + int(np.argmax(pw[k:j + 1]))
            vt.append(t_all[m]); vw.append(score[k:j + 1].max())
            k = j + 1
        vt, vw = np.array(vt), np.array(vw)

        # 각 음절 뒤로 목소리가 끊기는 시각 (롱노트 길이는 노트를 고른 뒤 다음 노트와 비교해 정한다)
        ends = np.empty(len(vt))
        for k, t in enumerate(vt):
            f0 = min(n - 1, max(0, frame_of(t)) + look)
            off = np.flatnonzero(~active[f0:])
            ends[k] = float(A.frame_time(f0 + off[0])) if len(off) else self.dur
        pitch = np.array([pitch_at(vocals, t) for t in vt])
        self.vocal = (vt, vw, ends, pitch)

        # ---- 목소리가 쉬는 구간의 반주 음 시작점 ----
        pad = int(0.35 * FPS)
        near_vocal = np.convolve(active.astype(float), np.ones(2 * pad + 1), mode='same') > 0
        quiet = ~near_vocal
        # 짧은 쉼은 비워 둔다
        edges = np.flatnonzero(np.diff(np.concatenate([[0], quiet.astype(int), [0]])))
        for a, b in zip(edges[::2], edges[1::2]):
            if (b - a) / FPS < INST_GAP_MIN: quiet[a:b] = False
        loud_i = A.moving_mean(rms_i, FPS * 0.5)
        audible = loud_i > 0.15 * np.percentile(loud_i, 90)
        pf, pw = A.pick_peaks(env_i, 0.8)
        fi = np.clip(pf.astype(int), 0, n - 1)
        keep = quiet[fi] & audible[fi]
        it, iw = A.frame_time(pf[keep]), np.minimum(pw[keep], 8.0)
        cen = A.moving_mean(cen_i, FPS * 0.05)[np.clip(fi[keep] + int(0.03 * FPS), 0, n - 1)]
        self.inst = (it, iw, cen)


def thin_fast(times, weights, gap):
    """센 것부터 고르되, 이미 고른 노트와 gap 초 안에 있으면 버린다 (시간순 정렬된 입력)."""
    taken = np.zeros(len(times), dtype=bool)
    for k in np.argsort(-weights):
        lo = np.searchsorted(times, times[k] - gap, side='right')
        hi = np.searchsorted(times, times[k] + gap, side='left')
        if not taken[lo:hi].any():
            taken[k] = True
    return np.flatnonzero(taken)


def generate(track, keys, diff, seed):
    P = DIFFS[diff]
    rng = random.Random(seed)
    vt, vw, vend, vpitch = track.vocal
    it, iw, icen = track.inst

    events = []   # (시각, 길이, 음높이(반음 단위, 없으면 None), 세기)
    ok = np.flatnonzero(vw >= P['floor'])
    picked = ok[thin_fast(vt[ok], vw[ok], P['gap'])]
    for j, k in enumerate(picked):
        # 한 음을 얼마나 끄는지: 목소리가 끊기거나, 다음으로 고른 노트가 올 때까지
        t_next = vt[picked[j + 1]] if j + 1 < len(picked) else track.dur
        dur = min(vend[k], t_next) - vt[k]
        ln = min(dur - 0.12, LONG_MAX) if dur >= LONG_MIN else 0.0
        st = 12 * np.log2(vpitch[k] / 55.0) if vpitch[k] > 0 else None
        events.append((float(vt[k]), float(ln), st, float(vw[k])))
    if len(it):
        strong = iw >= np.median(iw) if diff != 'Hard' else np.ones(len(iw), dtype=bool)
        idx = np.flatnonzero(strong)
        for k in idx[thin_fast(it[idx], iw[idx], P['inst_gap'])]:
            events.append((float(it[k]), 0.0, 12 * np.log2(max(icen[k], 60.0) / 55.0) * 0.5, float(iw[k])))
    events.sort()
    events = [e for e in events if 1.0 <= e[0] <= track.dur - 1.0]

    weights = np.array([e[3] for e in events]) if events else np.zeros(0)
    chord_thr = np.quantile(weights, 1 - P['chord']) if P['chord'] > 0 and len(weights) else 1e9

    notes = []
    busy = [-1.0] * keys          # 각 레인이 다시 비는 시각
    last_hit = [-9.0] * keys      # 각 레인을 마지막으로 친 시각
    lane, prev_st = rng.randrange(keys), None
    for t, ln, st, w in events:
        free = [l for l in range(keys) if busy[l] < t - 0.02]
        if not free:
            continue

        # 음이 올라가면 오른쪽, 내려가면 왼쪽, 같은 음이면 같은 자리
        if st is None or prev_st is None: step = rng.choice([-1, 0, 1])
        else:
            d = st - prev_st
            step = 0 if abs(d) < 0.7 else (2 if abs(d) >= 5 else 1) * (1 if d > 0 else -1)
        target = lane + step
        if target < 0: target = -target
        if target > keys - 1: target = 2 * (keys - 1) - target
        target = min(max(target, 0), keys - 1)

        # 같은 레인을 너무 빨리 연달아 치게 하지 않는다
        cands = [l for l in free if t - last_hit[l] >= P['jack']] or free
        best = min(abs(l - target) for l in cands)
        l = rng.choice([l for l in cands if abs(l - target) == best])

        note = dict(t=int(round(t * 1000)), lane=l)
        if ln > 0: note['len'] = int(round(ln * 1000))
        notes.append(note)
        busy[l] = t + ln + (0.12 if ln > 0 else 0.0)
        last_hit[l] = t + ln

        if ln == 0 and w >= chord_thr:
            free2 = [m for m in free if m != l and t - last_hit[m] >= P['jack']]
            if free2:
                mirror = keys - 1 - l
                m = mirror if mirror in free2 else rng.choice(free2)
                notes.append(dict(t=int(round(t * 1000)), lane=m))
                busy[m] = t
                last_hit[m] = t
        lane = l
        if st is not None: prev_st = st

    notes.sort(key=lambda n: (n['t'], n['lane']))
    span = max(1.0, (notes[-1]['t'] - notes[0]['t']) / 1000) if notes else 1.0
    nps = len(notes) / span
    level = int(min(20, max(1, round(nps * 3.2 + (1 if keys == 6 else 0)))))
    return dict(keys=keys, difficulty=diff, level=level, notes=notes)


def process(songs_dir, stems_dir, title):
    json_path = os.path.join(songs_dir, title, 'song.json')
    stem_path = os.path.join(stems_dir, title + '.npz')
    if not (os.path.exists(json_path) and os.path.exists(stem_path)):
        return
    with open(json_path, encoding='utf-8') as f:
        data = json.load(f)
    z = np.load(stem_path)
    track = Track(z['vocals'].astype(np.float32), z['inst'].astype(np.float32))

    seed = zlib.crc32(title.encode('utf-8'))
    data['charts'] = [generate(track, k, d, seed + k * 10 + n) for k in (4, 6) for n, d in enumerate(DIFFS)]
    data.pop('video', None)
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, separators=(',', ':'))

    c4 = data['charts'][:3]
    longs = sum(1 for n in c4[1]['notes'] if n.get('len'))
    print('%-22s 목소리 구간 %2.0f%% | 보컬 음절 %4d  반주 음 %4d | 4K E=%d N=%d H=%d (Normal 롱노트 %d)' %
          (title, track.vocal_ratio * 100, len(track.vocal[0]), len(track.inst[0]),
           len(c4[0]['notes']), len(c4[1]['notes']), len(c4[2]['notes']), longs), flush=True)


if __name__ == '__main__':
    songs_dir, stems_dir = sys.argv[1], sys.argv[2]
    A.calibrate()
    titles = sys.argv[3:] or sorted(os.listdir(songs_dir))
    for title in titles:
        try:
            process(songs_dir, stems_dir, title)
        except Exception as e:
            import traceback; traceback.print_exc()
            print('실패: %s (%s)' % (title, e), flush=True)
