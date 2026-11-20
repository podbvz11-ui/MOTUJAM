"""
음원(또는 영상)을 분석해서 모투잼 채보(song.json)를 자동으로 만든다.

사용법:
    py -3.12 autochart.py <원본 폴더 또는 파일> <Songs 폴더> [--artist 이름] [--force]

곡마다 <Songs 폴더>/<곡 이름>/ 에 audio.ogg, jacket.jpg, song.json 을 만든다.
이미 song.json 이 있는 곡은 --force 없이는 건드리지 않는다 (손으로 고친 채보 보호).
"""
import sys, os, json, subprocess, zlib, random
import numpy as np
import imageio_ffmpeg
from numpy.lib.stride_tricks import sliding_window_view

FF = imageio_ffmpeg.get_ffmpeg_exe()
SR, N, HOP = 22050, 1024, 128
FPS = SR / HOP
BIAS = 0.0                   # 검출 시각 - 실제 타격 시각 (초). 시작할 때 합성음으로 측정한다

DIFFS = {
    #          목표 초당 노트, 노트 최소 간격(초), 16분 가중치, 빈 구간 채우기 기준(16분 칸), 동시치기 비율, 스페이스 간격(마디), 스페이스 길이(칸)
    'Easy':   dict(nps=1.6, min_sp=0.24, w16=0.0,  fill=8, chord=0.00, period=8, space_len=8,  jack=4),
    'Normal': dict(nps=2.9, min_sp=0.15, w16=0.45, fill=8, chord=0.10, period=6, space_len=12, jack=2),
    'Hard':   dict(nps=4.5, min_sp=0.09, w16=0.75, fill=4, chord=0.22, period=4, space_len=12, jack=1),
}


def ffmpeg(args, capture=False):
    return subprocess.run([FF, '-hide_banner', '-loglevel', 'error', '-y'] + args, check=True, capture_output=capture)


def decode(path):
    out = ffmpeg(['-i', path, '-vn', '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'], capture=True).stdout
    return np.frombuffer(out, dtype=np.float32)


def moving_mean(a, n):
    n = max(1, int(n))
    return np.convolve(a, np.ones(n) / n, mode='same')


def features(x):
    """프레임별 타격 세기(env), 저음 타격 세기(low), 음량(rms), 음색 높이(centroid)."""
    frames = sliding_window_view(x, N)[::HOP]
    win = np.hanning(N).astype(np.float32)
    freqs = np.fft.rfftfreq(N, 1 / SR).astype(np.float32)
    nlow = int(160 / (SR / N)) + 1
    flux, low, rms, cen = [], [], [], []
    prev = None
    for i in range(0, len(frames), 4096):
        chunk = frames[i:i + 4096]
        mag = np.abs(np.fft.rfft(chunk * win, axis=1)).astype(np.float32)
        logm = np.log1p(50 * mag)
        first = logm[:1] if prev is None else prev
        d = np.maximum(np.diff(np.concatenate([first, logm]), axis=0), 0)
        flux.append(d.sum(1))
        low.append(d[:, :nlow].sum(1))
        prev = logm[-1:]
        rms.append(np.sqrt((chunk.astype(np.float32) ** 2).mean(1)))
        cen.append((mag * freqs).sum(1) / (mag.sum(1) + 1e-9))
    flux, low, rms, cen = map(np.concatenate, (flux, low, rms, cen))
    env = np.maximum(flux - moving_mean(flux, FPS * 0.4), 0)
    env = env / (env.std() + 1e-9)
    low = np.maximum(low - moving_mean(low, FPS * 0.4), 0)
    low = low / (low.std() + 1e-9)
    return env, low, rms, cen


def frame_time(f):
    return (np.asarray(f, dtype=np.float64) * HOP + N / 2) / SR - BIAS


def pick_peaks(env, thr=0.6):
    """타격 지점(프레임 위치, 세기). 포물선 보간으로 프레임보다 정밀하게 잡는다."""
    r = max(1, int(0.03 * FPS))
    mx = sliding_window_view(np.pad(env, r), 2 * r + 1).max(1)
    i = np.flatnonzero((env >= mx) & (env > thr))
    i = i[(i > 0) & (i < len(env) - 1)]
    if len(i) > 1: i = i[np.concatenate([[True], np.diff(i) > r])]
    a, b, c = env[i - 1], env[i], env[i + 1]
    d = 0.5 * (a - c) / np.minimum(a - 2 * b + c, -1e-9)
    return i + np.clip(d, -0.5, 0.5), b


def calibrate():
    """타격 시각을 알고 있는 합성음으로 검출 지연을 잰다."""
    global BIAS
    BIAS = 0.0
    rng = np.random.default_rng(1)
    x = (rng.standard_normal(SR * 12) * 0.002).astype(np.float32)
    true = np.arange(1.0, 11.0, 0.37)
    burst = (rng.standard_normal(int(SR * 0.08)) * np.exp(-np.arange(int(SR * 0.08)) / (SR * 0.015))).astype(np.float32)
    for t in true:
        s = int(round(t * SR))
        x[s:s + len(burst)] += burst
    env = features(x)[0]
    pf, pw = pick_peaks(env, 1.0)
    pt = frame_time(pf)
    diffs = [pt[np.argmin(np.abs(pt - t))] - t for t in true]
    BIAS = float(np.median(diffs))


def find_grid(env, low):
    """BPM, 첫 박 위치(초), 그리고 박자 맞춤 품질 정보를 구한다."""
    envc = np.minimum(env, 6.0)
    pf, pw = pick_peaks(env)
    strong = pw >= np.quantile(pw, 0.5)
    pts, pws = frame_time(pf[strong]), np.minimum(pw[strong], 6.0)
    fi = np.clip(np.round(pf[strong]).astype(int), 2, len(low) - 3)
    lws = np.minimum(np.max([low[fi + k] for k in (-2, -1, 0, 1, 2)], axis=0), 6.0)

    # 1) 자기상관으로 대략의 BPM
    e = envc - envc.mean()
    f = np.fft.rfft(e, 2 * len(e))
    ac = np.fft.irfft(f * np.conj(f))[:len(e)]
    bpms = np.arange(70, 200, 0.5)
    lags = 60 * FPS / bpms
    x = np.arange(len(ac))
    score = np.interp(lags, x, ac) + 0.5 * np.interp(2 * lags, x, ac) + 0.25 * np.interp(4 * lags, x, ac)
    prior = np.exp(-0.5 * (np.log2(bpms / 125) / 0.55) ** 2)
    base = float(bpms[np.argmax(score * prior)])

    B = 24
    fr = np.arange(len(envc))

    def fold(bpm):
        T = 60 * FPS / bpm
        h = np.bincount(((fr / T) % 1 * B).astype(int) % B, weights=envc, minlength=B)
        return h + 0.5 * (np.roll(h, 1) + np.roll(h, -1))

    def coarse(c):
        grid = np.arange(c * 0.97, c * 1.03, 0.01)
        bpm = float(grid[np.argmax([fold(b).max() for b in grid])])
        h = fold(bpm)
        frac = (np.argmax(h) + 0.5) / B
        return bpm, float(frame_time(frac * 60 * FPS / bpm))

    def lsq(bpm, t0):
        # 2) 강한 타격들이 16분음표 격자에 가장 잘 맞도록 BPM과 시작점을 최소제곱으로 맞춘다
        slot = 15 / bpm
        for tol in (0.3, 0.25, 0.2, 0.2):
            n = np.round((pts - t0) / slot)
            ok = np.abs(pts - (t0 + n * slot)) < tol * slot
            if ok.sum() < 20: break
            slot, t0 = np.polyfit(n[ok], pts[ok], 1, w=pws[ok])
        return 15 / slot, float(t0)

    def fit(bpm, t0):
        # 8분음표 격자 ±20ms 안에 들어오는 타격 비율 (우연히 맞는 비율은 뺀다)
        sp = 30 / bpm
        best = (-1, t0)
        for shift in (0.0, sp / 2):
            r = pts - t0 - shift
            r = r - np.round(r / sp) * sp
            hit = pws[np.abs(r) < 0.02].sum() / pws.sum()
            chance = 0.04 / sp
            s = (hit - chance) / (1 - chance)
            if s > best[0]: best = (s, t0 + shift)
        return best

    # 3) 셋잇단 느낌의 곡은 1.5배 / 2/3배 템포가 진짜 박자일 수 있으므로 후보를 비교한다
    best = None
    for ratio in (1, 1.5, 2 / 3, 0.75, 4 / 3, 2, 0.5):
        c = base * ratio
        if not 75 <= c <= 200: continue
        bpm, t0 = lsq(*coarse(c))
        s, t0 = fit(bpm, t0)
        s -= 0 if ratio == 1 else 0.04
        if best is None or s > best[0]: best = (s, bpm, t0)
    _, bpm, t0 = best
    slot = 15 / bpm

    # 4) 정박과 엇박 구분: 저음(킥)과 강한 타격이 더 많이 실리는 쪽이 정박
    n8 = np.round((pts - t0) / (2 * slot))
    inl = np.abs(pts - t0 - n8 * 2 * slot) < 0.03
    wt = pws + 3 * lws
    if wt[inl & (n8 % 2 == 1)].sum() > wt[inl & (n8 % 2 == 0)].sum():
        t0 += 2 * slot

    # 5) 템포가 곡 중간에 조금씩 흔들리는 경우를 따라간다 (2초마다 어긋남을 재서 누적 보정)
    centers = np.arange(0.0, pts.max() + 2, 2.0)
    corr = np.zeros(len(centers))
    c = 0.0
    for j, tc in enumerate(centers):
        m = (pts >= tc - 3) & (pts < tc + 3)
        if m.sum() >= 8:
            r = pts[m] - t0 - c
            r = r - np.round(r / slot) * slot
            r = r[np.abs(r) < 0.3 * slot]
            if len(r) >= 6: c += 0.6 * float(np.median(r))
        corr[j] = c
    corr = moving_mean(np.pad(corr, 1, mode='edge'), 3)[1:-1]
    def within(cr):
        g = np.round((pts - np.interp(pts, centers, cr) - t0) / slot)
        t = t0 + g * slot
        return pts - (t + np.interp(t, centers, cr))

    # 따라간 격자가 고정 격자보다 확실히 잘 맞을 때만 쓴다 (보컬 잡음을 쫓아 격자가 흔들리는 것 방지)
    rigid = np.zeros_like(corr)
    gain = (np.abs(within(corr)) < 0.025).mean() - (np.abs(within(rigid)) < 0.025).mean()
    if np.abs(corr).max() < 0.012 or gain < 0.08: corr = rigid

    def grid_time(g):
        t = t0 + np.asarray(g, dtype=np.float64) * slot
        return t + np.interp(t, centers, corr)

    # 품질: 강한 타격이 최종 격자에서 얼마나 떨어져 있는가
    res = within(corr)
    quality = dict(within25=float((np.abs(res) < 0.025).mean() * 100), med=float(np.median(np.abs(res)) * 1000),
                   drift=float(np.abs(corr).max() * 1000))
    return bpm, t0, grid_time, quality


class Song:
    def __init__(self, x):
        self.dur = len(x) / SR
        self.env, self.low, self.rms, self.cen = features(x)
        self.bpm, self.t0, grid_time, self.quality = find_grid(self.env, self.low)
        self.slot = 60 / self.bpm / 4                    # 16분음표 길이(초)

        # 16분음표 격자: g % 4 == 0 이 정박
        g_min = int(np.ceil((1.5 - self.t0) / self.slot / 4)) * 4
        g_max = int((self.dur - 1.5 - self.t0) / self.slot)
        self.g = np.arange(g_min, g_max + 1)
        self.t = grid_time(self.g)
        fc = (self.t + BIAS) * SR / HOP - N / 2 / HOP
        r = max(1, int(round(0.3 * self.slot * FPS)))
        n = len(self.env)
        envc = np.minimum(self.env, 6.0)
        self.S = np.array([envc[max(0, int(round(c)) - r):min(n, int(round(c)) + r + 1)].max(initial=0) for c in fc])
        fi = np.clip(np.round(fc).astype(int), 0, n - 1)
        smooth = moving_mean(self.rms, FPS * 1.0)
        self.loud = smooth[fi]
        self.rms_s = moving_mean(self.rms, FPS * 0.08)[fi]
        self.cen_s = moving_mean(self.cen, FPS * 0.05)[np.clip(fi + int(FPS * 0.03), 0, n - 1)]
        self.active = self.loud > 0.25 * np.median(smooth[smooth > 1e-4])

    def ms(self, i):
        return int(round(float(self.t[i]) * 1000))


def generate(song, keys, diff, seed):
    P = DIFFS[diff]
    rng = random.Random(seed)
    G = len(song.g)
    lvl = song.g % 4
    slot = song.slot

    # ---- 1. 어느 칸에 노트를 둘지 고른다 ----
    w = np.where(lvl == 0, 1.0, np.where(lvl == 2, 0.8, P['w16']))
    if 2 * slot < P['min_sp']: w = np.where(lvl == 2, 0.0, w)
    if slot < P['min_sp']: w = np.where(lvl % 2 == 1, 0.0, w)
    score = song.S * w * song.active
    active_sec = float(song.active.sum()) * slot
    target = int(P['nps'] * active_sec)
    order = np.sort(score[score > 0.05])[::-1]
    thr = order[min(target, len(order) - 1)] if len(order) else 1e9
    sel = score >= thr
    # 빈 구간을 채울 때도 실제로 소리가 나는 박에만 노트를 둔다
    fill_thr = 0.3 * float(np.median(song.S[sel])) if sel.any() else 1e9

    # ---- 2. 빈 구간은 롱노트나 정박 노트로 채운다 ----
    longs = {}
    idx = np.flatnonzero(sel)
    for a, b in zip(idx[:-1], idx[1:]):
        gap = b - a
        if gap < P['fill'] or not song.active[a:b].all():
            continue
        start = a
        sustained = song.rms_s[min(a + 2, G - 1)] > 0.55 * song.rms_s[a] and song.rms_s[min(a + 4, G - 1)] > 0.5 * song.rms_s[a]
        if gap >= 8 and sustained and rng.random() < 0.65:
            ln = min(gap - 2, 16)
            ln -= ln % 2
            longs[a] = ln
            start = a + ln + 2
        for i in range(start + 1, b):
            if lvl[i] == 0 and b - i >= 2 and i - start >= 2 and song.S[i] >= fill_thr:
                sel[i] = True

    # ---- 3. 레인 배치 ----
    busy = [-10] * keys
    lane, prev_i, prev_c = rng.randrange(keys), -99, None
    picked = song.S[sel]
    chord_thr = np.quantile(picked, 1 - P['chord']) if P['chord'] > 0 and len(picked) else 1e9
    triple_thr = np.quantile(picked, 0.96) if keys == 6 and diff == 'Hard' and len(picked) else 1e9
    notes = []

    for i in np.flatnonzero(sel):
        ln = longs.get(i, 0)
        if i + ln >= G: ln = 0
        free = [l for l in range(keys) if busy[l] < i]
        if not free:
            continue

        # 음색이 높아지면 오른쪽, 낮아지면 왼쪽으로 움직인다
        c = song.cen_s[i]
        if prev_c is None or abs(c - prev_c) < 0.08 * prev_c: step = rng.choice([-1, 1])
        else: step = (2 if abs(c - prev_c) > 0.3 * prev_c else 1) * (1 if c > prev_c else -1)
        target_lane = lane + step
        if target_lane < 0: target_lane = -target_lane
        if target_lane > keys - 1: target_lane = 2 * (keys - 1) - target_lane
        target_lane = min(max(target_lane, 0), keys - 1)

        cands = [l for l in free if not (l == lane and i - prev_i <= P['jack'])] or free
        best = min(abs(l - target_lane) for l in cands)
        l = rng.choice([l for l in cands if abs(l - target_lane) == best])
        note = dict(t=song.ms(i), lane=l)
        if ln: note['len'] = song.ms(i + ln) - song.ms(i)
        notes.append(note)
        busy[l] = i + ln + 1 if ln else i

        # 강한 박은 동시치기
        if not ln and lvl[i] % 2 == 0 and song.S[i] >= chord_thr:
            extra = 2 if song.S[i] >= triple_thr else 1
            for _ in range(extra):
                free2 = [m for m in free if busy[m] < i]
                if not free2: break
                mirror = keys - 1 - l
                m = mirror if mirror in free2 else rng.choice(free2)
                notes.append(dict(t=song.ms(i), lane=m))
                busy[m] = i
        lane, prev_i, prev_c = l, i, c

    notes.sort(key=lambda n: (n['t'], n.get('lane', -1)))
    nps = len(notes) / max(active_sec, 1)
    level = int(min(20, max(1, round(nps * 2.6 + (1 if keys == 6 else 0)))))
    return dict(keys=keys, difficulty=diff, level=level, notes=notes)


def process(src, songs_dir, artist, force):
    title = os.path.splitext(os.path.basename(src))[0]
    out = os.path.join(songs_dir, title)
    json_path = os.path.join(out, 'song.json')
    if os.path.exists(json_path) and not force:
        print('건너뜀(이미 있음): ' + title, flush=True)
        return
    os.makedirs(out, exist_ok=True)

    audio = os.path.join(out, 'audio.ogg')
    if not os.path.exists(audio):
        ffmpeg(['-i', src, '-vn', '-ac', '2', '-ar', '44100', '-c:a', 'libvorbis', '-q:a', '5', audio])
    x = decode(audio)                       # 게임이 재생할 파일 그대로 분석해야 시간이 어긋나지 않는다
    song = Song(x)

    jacket = os.path.join(out, 'jacket.jpg')
    if not os.path.exists(jacket):
        try:
            ffmpeg(['-ss', '%.1f' % (song.dur * 0.3), '-i', src, '-frames:v', '1',
                    '-vf', 'crop=ih:ih,scale=512:512', '-q:v', '3', jacket])
        except subprocess.CalledProcessError:
            pass                            # 영상이 없는 음원이면 자켓 없이 진행

    seed = zlib.crc32(title.encode('utf-8'))
    charts = [generate(song, k, d, seed + k * 10 + n) for k in (4, 6) for n, d in enumerate(DIFFS)]
    data = dict(title=title, artist=artist, audio='audio.ogg',
                jacket='jacket.jpg' if os.path.exists(jacket) else '',
                video='video.mp4' if os.path.exists(os.path.join(out, 'video.mp4')) else '',
                bpm=round(song.bpm, 3), offset=0, charts=charts)
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, separators=(',', ':'))

    q = song.quality
    counts = ' '.join('%s=%d' % (c['difficulty'][0], len(c['notes'])) for c in charts[:3])
    print('%-22s BPM %-8.3f 첫박 %.3fs | 격자 ±25ms 안 %3.0f%%  중간오차 %4.1fms  템포흔들림 %3.0fms | 4K %s' %
          (title, song.bpm, song.t0 % (4 * song.slot), q['within25'], q['med'], q['drift'], counts), flush=True)


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    force = '--force' in sys.argv
    artist = sys.argv[sys.argv.index('--artist') + 1] if '--artist' in sys.argv else ''
    if '--artist' in sys.argv: args.remove(artist)
    src, songs_dir = args[0], args[1]
    calibrate()
    print('검출 지연 보정: %.1fms' % (BIAS * 1000), flush=True)
    exts = ('.mp4', '.mkv', '.webm', '.mov', '.mp3', '.ogg', '.wav', '.m4a', '.flac')
    files = [src] if os.path.isfile(src) else sorted(os.path.join(src, f) for f in os.listdir(src) if f.lower().endswith(exts))
    for f in files:
        try:
            process(f, songs_dir, artist, force)
        except Exception as e:
            import traceback; traceback.print_exc()
            print('실패: %s (%s)' % (os.path.basename(f), e), flush=True)
