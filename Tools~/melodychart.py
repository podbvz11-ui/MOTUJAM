"""
노래의 멜로디(반주 속 멜로디·화음 악기)에 맞춘 채보를 만든다.
부르는 사람의 박자는 쓰지 않는다. 반주(MR)는 원곡 그대로라 박자가 정확하기 때문이다.

사용법 (separate.py 로 stems 를 먼저 만들어야 한다):
    python melodychart.py <Songs 폴더> <stems 폴더> [곡 이름 ...]

각 곡의 song.json 에서 제목/음원/자켓/BPM 은 그대로 두고 charts 만 새로 쓴다.
"""
import sys, os, json, zlib, random
import numpy as np
import autochart as A
from autochart import SR, HOP, FPS

DIFFS = {
    #          목표 초당 노트, 노트 사이 최소 간격(초), 동시치기 비율, 같은 레인 연타를 피하는 간격(초)
    'Easy':   dict(nps=1.5, gap=0.30, chord=0.00, jack=0.40),
    'Normal': dict(nps=2.6, gap=0.17, chord=0.07, jack=0.26),
    'Hard':   dict(nps=4.0, gap=0.10, chord=0.15, jack=0.15),
}
MERGE = 0.06        # 이 시간(초) 안에 붙은 시작점은 한 음으로 본다
SNAP = 0.035        # 박자 격자에서 이 시간(초) 안쪽으로 어긋난 노트는 격자에 붙인다
SNAP_MIN_QUALITY = 60   # 박자 격자가 이 정도(강한 타격의 %가 ±25ms 안)로 잘 맞는 곡에만 붙인다
LONG_MIN = 0.6      # 한 음이 이 시간(초) 이상 울리면 롱노트
LONG_MAX = 3.0


def melody_pitch(x, t):
    """t 직후 구간에서 가장 두드러진 음의 높이(반음 단위, 55Hz = 0). 못 찾으면 None.
    여러 배음을 함께 보는 방식이라 화음 속에서도 가장 센 음을 고른다."""
    s = int((t + 0.03) * SR)
    seg = x[s:s + 4096].astype(np.float64)
    if len(seg) < 4096 or np.abs(seg).max() < 1e-4:
        return None
    mag = np.abs(np.fft.rfft(seg * np.hanning(len(seg))))
    freqs = np.fft.rfftfreq(len(seg), 1 / SR)
    cands = 110 * 2 ** (np.arange(0, 36 * 4) / 48)          # 110Hz ~ 880Hz, 1/4 반음 간격
    sal = np.zeros(len(cands))
    for h in range(1, 6):
        sal += 0.8 ** (h - 1) * np.interp(cands * h, freqs, mag)
    k = int(np.argmax(sal))
    if sal[k] < 3 * np.median(sal):
        return None
    return 12 * np.log2(cands[k] / 55.0)


class Track:
    """곡 하나의 분석 결과: 멜로디 음의 시작점, 세기, 울림이 끝나는 시각, 음높이."""

    def __init__(self, other):
        self.dur = len(other) / SR
        env, _, rms, _ = A.features(other)
        n = len(env)

        rs = A.moving_mean(rms, FPS * 0.03)
        loud = A.moving_mean(rms, FPS * 0.5)
        active = loud > 0.12 * np.percentile(loud, 95)
        self.active_sec = float(active.mean()) * self.dur
        self.active_ratio = float(active.mean())

        pf, pw = A.pick_peaks(env, 0.5)
        fi = np.clip(pf.astype(int), 0, n - 1)
        keep = active[fi]
        pf, pw = pf[keep], np.minimum(pw[keep], 8.0)
        t_all = A.frame_time(pf)

        # 바로 붙은 시작점은 하나로 합친다
        times, weights, frames = [], [], []
        k = 0
        while k < len(t_all):
            j = k
            while j + 1 < len(t_all) and t_all[j + 1] - t_all[k] < MERGE: j += 1
            m = k + int(np.argmax(pw[k:j + 1]))
            times.append(t_all[m]); weights.append(pw[k:j + 1].max()); frames.append(int(pf[m]))
            k = j + 1
        self.t = np.array(times)
        self.w = np.array(weights)

        # 울림이 끝나는 시각: 소리가 시작 직후 크기의 절반 아래로 떨어질 때
        look = int(0.08 * FPS)
        ends = np.empty(len(self.t))
        for i, f in enumerate(frames):
            peak = rs[f:f + look].max() if f < n else 0
            after = rs[f + look:]
            drop = np.flatnonzero(after < 0.5 * peak)
            ends[i] = float(A.frame_time(f + look + drop[0])) if len(drop) else self.dur
        self.end = ends
        self.pitch = [melody_pitch(other, t) for t in self.t]


def snap_times(times, song):
    """16분음표 격자나 셋잇단 8분음표 격자 중 가까운 쪽에 붙인다. SNAP 보다 멀면 그대로 둔다."""
    out = times.copy()
    g = np.interp(times, song.t, np.arange(len(song.t)))           # 16분 격자 번호 (소수)
    for i, x in enumerate(g):
        best = None
        for step in (1.0, 4 / 3):                                    # 16분, 셋잇단 8분 (= 16분 4/3칸)
            k = np.round(x / step) * step
            t = float(np.interp(k, np.arange(len(song.t)), song.t))
            if best is None or abs(t - times[i]) < abs(best - times[i]): best = t
        if abs(best - times[i]) <= SNAP:
            out[i] = best
    return out


def pick(track, P):
    """센 음부터 고르되 최소 간격을 지키고, 목표 개수에 이르면 멈춘다."""
    target = int(P['nps'] * track.active_sec)
    taken = np.zeros(len(track.t), dtype=bool)
    count = 0
    for k in np.argsort(-track.w):
        if count >= target: break
        lo = np.searchsorted(track.t, track.t[k] - P['gap'], side='right')
        hi = np.searchsorted(track.t, track.t[k] + P['gap'], side='left')
        if not taken[lo:hi].any():
            taken[k] = True
            count += 1
    return np.flatnonzero(taken)


def generate(track, keys, diff, seed):
    P = DIFFS[diff]
    rng = random.Random(seed)
    chosen = pick(track, P)

    events = []   # (시각, 길이, 음높이, 세기)
    for j, k in enumerate(chosen):
        t = track.t[k]
        t_next = track.t[chosen[j + 1]] if j + 1 < len(chosen) else track.dur
        dur = min(track.end[k], t_next) - t
        ln = min(dur - 0.12, LONG_MAX) if dur >= LONG_MIN else 0.0
        events.append((float(t), float(ln), track.pitch[k], float(track.w[k])))
    events = [e for e in events if 1.0 <= e[0] <= track.dur - 1.0]

    weights = np.array([e[3] for e in events]) if events else np.zeros(0)
    chord_thr = np.quantile(weights, 1 - P['chord']) if P['chord'] > 0 and len(weights) else 1e9

    notes = []
    busy = [-1.0] * keys
    last_hit = [-9.0] * keys
    lane, prev_st = rng.randrange(keys), None
    for t, ln, st, w in events:
        free = [l for l in range(keys) if busy[l] < t - 0.02]
        if not free:
            continue

        # 멜로디가 올라가면 오른쪽, 내려가면 왼쪽, 같은 음이면 같은 자리
        if st is None or prev_st is None: step = rng.choice([-1, 1])
        else:
            d = st - prev_st
            step = 0 if abs(d) < 0.5 else (2 if abs(d) >= 5 else 1) * (1 if d > 0 else -1)
        target = lane + step
        if target < 0: target = -target
        if target > keys - 1: target = 2 * (keys - 1) - target
        target = min(max(target, 0), keys - 1)

        cands = [l for l in free if t - last_hit[l] >= P['jack']] or free
        best = min(abs(l - target) for l in cands)
        l = rng.choice([l for l in cands if abs(l - target) == best])

        note = dict(t=int(round(t * 1000)), lane=l)
        if ln > 0: note['len'] = int(round(ln * 1000))
        notes.append(note)
        busy[l] = t + ln + (0.12 if ln > 0 else 0.0)
        last_hit[l] = t + ln

        # 센 음은 동시치기
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
    level = int(min(20, max(1, round(len(notes) / span * 3.2 + (1 if keys == 6 else 0)))))
    return dict(keys=keys, difficulty=diff, level=level, notes=notes)


def process(songs_dir, stems_dir, title):
    json_path = os.path.join(songs_dir, title, 'song.json')
    stem_path = os.path.join(stems_dir, title + '.npz')
    if not (os.path.exists(json_path) and os.path.exists(stem_path)):
        return
    z = np.load(stem_path)
    if 'other' not in z.files:
        print('건너뜀(멜로디 분리 전): ' + title, flush=True)
        return
    with open(json_path, encoding='utf-8') as f:
        data = json.load(f)
    track = Track(z['other'].astype(np.float32))

    # 원곡 박자 격자를 찾아, 잘 맞는 곡이면 노트를 격자에 붙인다
    song = A.Song(A.decode(os.path.join(songs_dir, title, data['audio'])))
    quality = song.quality['within25']
    snapped = quality >= SNAP_MIN_QUALITY
    if snapped:
        before = track.t
        track.t = snap_times(track.t, song)
        track.end = track.end + (track.t - before)
        order = np.argsort(track.t, kind='stable')
        track.t, track.w, track.end = track.t[order], track.w[order], track.end[order]
        track.pitch = [track.pitch[i] for i in order]

    seed = zlib.crc32(title.encode('utf-8'))
    data['charts'] = [generate(track, k, d, seed + k * 10 + n) for k in (4, 6) for n, d in enumerate(DIFFS)]
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, separators=(',', ':'))

    c4 = data['charts'][:3]
    longs = sum(1 for n in c4[1]['notes'] if n.get('len'))
    print('%-22s 멜로디 구간 %2.0f%% | 박자 격자 %3.0f%% %s | 4K E=%d N=%d H=%d (Normal 롱노트 %d)' %
          (title, track.active_ratio * 100, quality, '격자 붙임' if snapped else '그대로   ',
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
