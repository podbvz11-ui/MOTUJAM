"""
가사(목소리)와 멜로디에 맞춘 채보를 만든다.

- 목소리가 나오는 동안: 가사 음절이 시작되는 순간에만 노트를 둔다. 멜로디 노트는 두지 않는다.
- 목소리가 쉬는 동안(전주·간주·후주): 반주의 멜로디 악기 음에 노트를 둔다.

음절은 목소리의 음높이를 10ms 단위로 추적해서 나눈다:
  1) 소리가 끊겼다가 다시 시작하는 곳 (자음 뒤의 모음 시작)
  2) 이어 부르는 중에 소리가 잠깐 작아졌다 커지는 곳 (ㄴ·ㅁ·ㄹ 같은 자음)
  3) 이어 부르는 중에 음높이가 바뀌는 곳

사용법 (separate.py 로 stems 를 먼저 만들어야 한다):
    python songchart.py <Songs 폴더> <stems 폴더> [곡 이름 ...]
"""
import sys, os, json, zlib, random
import numpy as np
from numpy.lib.stride_tricks import sliding_window_view
import autochart as A
import melodychart as M
from autochart import SR

HOPV, WIN, NFFT = 220, 1024, 2048        # 목소리 분석: 약 10ms 간격
DT = HOPV / SR
LAG_LO, LAG_HI = int(SR / 1000), int(SR / 75)
VBIAS = 0.0                               # 음절 검출 시각 보정값 (초). calibrate_vocal 이 정한다

DIFFS = {
    #          음절 점수 하한, 노트 최소 간격(초), 간주 멜로디 초당 노트 / 최소 간격, 동시치기 비율, 같은 레인 연타 회피(초)
    'Easy':   dict(floor=2.0, gap=0.34, mel_nps=1.4, mel_gap=0.30, chord=0.00, jack=0.42),
    'Normal': dict(floor=1.4, gap=0.19, mel_nps=2.4, mel_gap=0.17, chord=0.05, jack=0.27),
    'Hard':   dict(floor=1.0, gap=0.075, mel_nps=3.6, mel_gap=0.075, chord=0.12, jack=0.16),
    # Hard 가 이미 가사 음절을 거의 다 쓰므로, 그 위 난이도는 반주가 치는 박자에 노트를 더 넣고 동시치기를 늘린다.
    #   fill: 가사 노트 수 대비 더 넣을 비율 / fill_hit: 반주가 이만큼 세게 치는 칸만 / fill_gap: 다른 노트와의 최소 간격(초) / fill_even: 8분 자리만
    'VeryHard': dict(floor=1.0, gap=0.075, mel_nps=5.0, mel_gap=0.07, chord=0.25, jack=0.13, fill=0.50, fill_hit=0.5, fill_gap=0.11, fill_even=True),
    'Extreme':  dict(floor=1.0, gap=0.06, mel_nps=6.5, mel_gap=0.06, chord=0.40, jack=0.10, fill=1.00, fill_hit=0.4, fill_gap=0.07, fill_even=False),
}
LONG_MIN, LONG_MAX = 0.6, 3.5
GAP_MIN = 2.5        # 목소리가 이 시간(초) 이상 쉬어야 멜로디 노트를 넣는다
GAP_EDGE = 0.25      # 목소리 앞뒤로 이만큼은 멜로디 노트를 비운다


def f0_track(x):
    """10ms 마다 (음높이[반음, 55Hz=0], 주기성 0~1, 음량). 주기성이 높으면 노래하는 목소리다."""
    frames = sliding_window_view(x, WIN)[::HOPV]
    w = np.hanning(WIN)
    wac = np.fft.irfft(np.abs(np.fft.rfft(w, NFFT)) ** 2)[:LAG_HI + 2]
    wac = wac / wac[0]
    st, per, rms = [], [], []
    for i in range(0, len(frames), 2048):
        fr = frames[i:i + 2048].astype(np.float64)
        fr = fr - fr.mean(1, keepdims=True)
        rms.append(np.sqrt((fr ** 2).mean(1)))
        ac = np.fft.irfft(np.abs(np.fft.rfft(fr * w, NFFT)) ** 2)[:, :LAG_HI + 2]
        acn = ac / (ac[:, :1] + 1e-12) / wac
        seg = acn[:, LAG_LO:LAG_HI]
        mx = seg.max(1)
        first = np.argmax(seg >= 0.9 * mx[:, None], axis=1)        # 옥타브 아래로 잘못 잡지 않도록 가장 짧은 주기를 고른다
        s = np.zeros(len(fr)); p = np.zeros(len(fr))
        for r in np.flatnonzero(mx > 0.3):
            a = first[r]
            b = min(seg.shape[1], a + (a + LAG_LO) // 4 + 2)
            k = a + int(np.argmax(seg[r, a:b])) + LAG_LO
            y0, y1, y2 = acn[r, k - 1], acn[r, k], acn[r, k + 1]
            d = 0.5 * (y0 - y2) / min(y0 - 2 * y1 + y2, -1e-9)
            s[r] = 12 * np.log2(SR / (k + np.clip(d, -0.5, 0.5)) / 55.0)
            p[r] = y1
        st.append(s); per.append(p)
    return np.concatenate(st), np.concatenate(per), np.concatenate(rms)


def ftime(i):
    return (np.asarray(i, dtype=np.float64) * HOPV + WIN / 2) / SR - VBIAS


def runs_of(mask):
    e = np.flatnonzero(np.diff(np.concatenate([[0], mask.astype(np.int8), [0]])))
    return list(zip(e[::2], e[1::2]))


def median5(a):
    p = np.pad(a, 2, mode='edge')
    return np.median(sliding_window_view(p, 5), axis=1)


class Vocal:
    """목소리 분석: 음절 시작점(시각, 점수, 끝나는 시각, 음높이)과 목소리 구간."""

    def __init__(self, x):
        st, per, rms = f0_track(x)
        n = len(st)
        ref = np.percentile(rms[rms > 1e-5], 95) if (rms > 1e-5).any() else 1.0
        voiced = (per >= 0.5) & (rms >= 0.035 * ref)
        for a, b in runs_of(~voiced):                       # 30ms 이하의 끊김은 잇는다
            if 0 < a and b < n and b - a <= 3: voiced[a:b] = True
        for a, b in runs_of(voiced):                        # 70ms 미만의 짧은 소리는 버린다
            if b - a < 7: voiced[a:b] = False
        rs = A.moving_mean(rms, 3)
        self.voiced, self.rms, self.n = voiced, rms, n

        def rise(lo, hi):
            """[lo, hi) 에서 소리가 가장 가파르게 커지는 프레임."""
            lo, hi = max(1, lo), min(n - 1, hi)
            if hi <= lo: return lo
            return lo + int(np.argmax(rs[lo + 1:hi + 1] - rs[lo - 1:hi - 1]))

        cands = []      # (프레임, 시각, 점수, 종류, 구간 끝 프레임)
        prev_end = -1000
        for a, b in runs_of(voiced):
            local = []
            silence = (a - prev_end) * DT
            local.append((rise(a - 5, a + 4), 3.0 if silence >= 0.2 else 2.2, 'start'))
            prev_end = b

            # 이어 부르는 중 소리가 작아졌다 커지는 곳
            for i in range(a + 6, b - 6):
                if rs[i] > rs[max(a, i - 4):i + 5].min(): continue
                before, after = rs[max(a, i - 15):i].max(), rs[i + 1:min(b, i + 16)].max()
                ratio = rs[i] / (min(before, after) + 1e-12)
                if ratio < 0.62:
                    local.append((rise(i, i + 9), 1.2 + 1.5 * (1 - ratio), 'dip'))

            # 이어 부르는 중 음높이가 바뀌는 곳 (떨림은 무시: 70ms 이상 한쪽으로 벗어나야 한다)
            sm = median5(st[a:b])
            note_start, j = 0, 5
            while j < len(sm) - 7:
                refp = np.median(sm[note_start:j])
                dev = sm[j:j + 7] - refp
                if (dev > 0.75).all() or (dev < -0.75).all():
                    local.append((a + j, 1.0 + min(abs(float(np.median(dev))), 4.0) / 4.0, 'pitch'))
                    note_start = j
                    j += 7
                else:
                    j += 1

            # 90ms 안에 붙은 후보는 점수가 높은 것만 남긴다
            local.sort()
            kept = []
            for c in local:
                if kept and c[0] - kept[-1][0] < 9:
                    if c[1] > kept[-1][1] and kept[-1][2] != 'start': kept[-1] = c
                else:
                    kept.append(c)
            for q, (f, score, kind) in enumerate(kept):
                seg_end = kept[q + 1][0] if q + 1 < len(kept) else b
                t = float(ftime(f)) - (0.01 if kind == 'pitch' else 0.0)
                cands.append((f, t, score, kind, seg_end, a, b))

        # 전혀 다른 방식(소리 스펙트럼이 갑자기 바뀌는 순간)으로도 같은 자리에서 시작이 잡히는지 대조한다.
        # 음높이 변화로만 잡힌 후보는 대조에서 확인된 것만 쓰고, 자음 후보는 확인되지 않으면 점수를 낮춘다.
        pf, _ = A.pick_peaks(A.features(x)[0], 1.0)
        flux_t = A.frame_time(pf)
        checked = []
        for c in cands:
            k = np.searchsorted(flux_t, c[1])
            near = min(abs(flux_t[j] - c[1]) for j in (k - 1, k) if 0 <= j < len(flux_t)) if len(flux_t) else 9.0
            ok = near <= 0.05
            if c[3] == 'pitch' and not ok: continue
            score = c[2] * (0.7 if c[3] == 'dip' and not ok else 1.0)
            checked.append((c[0], c[1], score) + c[3:])
        # 버려진 후보가 있으면 앞 음절의 끝을 다음에 남은 음절까지로 늘린다
        cands = []
        for q, c in enumerate(checked):
            nxt = checked[q + 1] if q + 1 < len(checked) else None
            seg_end = nxt[0] if nxt is not None and nxt[5] == c[5] else c[6]
            cands.append(c[:4] + (seg_end,) + c[5:])

        self.t = np.array([c[1] for c in cands])
        self.score = np.array([c[2] for c in cands])
        self.kind = [c[3] for c in cands]
        self.end = np.array([float(ftime(c[4])) for c in cands])      # 다음 음절이 오거나 목소리가 끊기는 시각
        self.run_end = np.array([float(ftime(c[6])) for c in cands])  # 목소리가 끊기는 시각
        self.pitch = []
        for c in cands:
            seg = st[min(c[0] + 3, c[4] - 1):min(c[0] + 14, c[4])]
            self.pitch.append(float(np.median(seg)) if len(seg) else None)

        # 목소리 구간: 노래하는 소리 + 작게 내는 소리 앞뒤로 여유를 둔다
        soft = A.moving_mean(rms, 20) > 0.05 * ref
        act = voiced | soft
        self.voiced_sec = float(voiced.sum()) * DT
        self.active = act

    def melody_gaps(self):
        """멜로디 노트를 넣어도 되는 구간 [(시작, 끝), ...] (초)."""
        gaps = []
        for a, b in runs_of(~self.active):
            ta, tb = float(ftime(a)) + GAP_EDGE, float(ftime(b)) - GAP_EDGE
            if a == 0: ta = 0.0
            if b == self.n: tb = float(ftime(b))
            if tb - ta >= GAP_MIN: gaps.append((ta, tb))
        return gaps


def calibrate_vocal():
    """시작 시각을 아는 합성음(배음이 있는 음, 15ms 에 걸쳐 커짐)으로 음절 검출 지연을 잰다."""
    global VBIAS
    VBIAS = 0.0
    rng = np.random.default_rng(2)
    x = (rng.standard_normal(SR * 14) * 0.0005).astype(np.float32)
    true = np.arange(1.0, 13.0, 0.53)
    m = int(SR * 0.3)
    tt = np.arange(m) / SR
    for q, t0 in enumerate(true):
        f = 180 * 2 ** ((q % 12) / 12)
        tone = sum(np.sin(2 * np.pi * f * h * tt) / h for h in range(1, 6))
        env = np.minimum(tt / 0.015, 1.0) * np.minimum((0.3 - tt) / 0.03, 1.0)
        s = int(round(t0 * SR))
        x[s:s + m] += (0.2 * tone * env).astype(np.float32)
    v = Vocal(x)
    starts = np.array([t for t, k in zip(v.t, v.kind) if k == 'start'])
    diffs = [starts[np.argmin(np.abs(starts - t0))] - t0 for t0 in true]
    VBIAS = float(np.median(diffs)) - 0.008        # 소리가 커지기 시작하고 약 8ms 뒤를 음절 시작으로 본다
    return VBIAS, float(np.std(diffs))


SNAP_MIN_QUALITY = 60     # 박자 격자가 이 정도(강한 타격의 %가 ±25ms 안)로 잡힌 곡만 박자에 붙인다


def grid_pos(times, song):
    """시각 -> 16분음표 격자 번호(소수). 4의 배수가 정박, 짝수가 8분음표 자리."""
    return song.g[0] + np.interp(times, song.t, np.arange(len(song.t)))


def grid_to_time(g, song):
    return np.interp(np.asarray(g, dtype=np.float64) - song.g[0], np.arange(len(song.t)), song.t)


def snap_grid(times, song):
    """가장 가까운 16분음표 자리에 붙인다 (반주 음)."""
    return grid_to_time(np.round(grid_pos(times, song)), song)


def snap_vocal(times, song, home=0):
    """가사 음절을 박자에 붙인다. 가사는 대부분 8분음표 간격으로 오므로 그 자리(home: 0 = 짝수 칸, 1 = 홀수 칸)를
    우선하고, 거기서 16분의 0.6칸보다 멀 때만 그 사이의 16분 자리로 보낸다."""
    g = grid_pos(times, song) - home
    k8 = 2 * np.round(g / 2)
    k = np.where(np.abs(g - k8) <= 0.6, k8, 2 * np.floor(g / 2) + 1)
    return grid_to_time(k + home, song)


ODD_COST = 0.2        # 가사가 주로 놓이는 칸(8분 자리)이 아닌 칸에 붙일 때의 벌점 (칸² 단위)
HIT_BONUS = 0.2       # 반주가 실제로 '쿵' 하고 치는 칸에 붙일 때의 가산점
PHRASE_GAP = 0.6      # 음절 사이가 이 시간(초) 이상 비면 다른 구절로 본다
PHRASE_SHIFT = 3.0    # 구절 전체를 앞뒤로 미는 것에 대한 벌점 계수


def slot_hits(song, accomp):
    """16분 칸마다 반주(목소리를 뺀 소리)가 얼마나 세게 치는지 0~1 로."""
    env = np.minimum(A.features(accomp)[0], 6.0)
    fc = (song.t + A.BIAS) * SR / A.HOP - A.N / 2 / A.HOP
    r = max(1, int(round(0.25 * song.slot * A.FPS)))
    n = len(env)
    s = np.array([env[max(0, int(round(c)) - r):min(n, int(round(c)) + r + 1)].max(initial=0) for c in fc])
    return np.clip(s / (np.quantile(s, 0.85) + 1e-9), 0, 1)


def align_vocal(times, song, home, hits):
    """가사 음절을 박자 칸에 배정한다. 음절을 하나씩 가까운 칸에 붙이는 대신 구절 단위로 한꺼번에 맞춘다.
      - 구절 전체가 조금 늦거나 빠르면(부르는 사람이 구절마다 다르게 민다) 구절째로 당겨서 본다
      - 잇따른 두 음절이 같은 칸에 겹치지 않게 한다
      - 어느 칸인지 애매한 음절은 반주가 실제로 치는 칸, 그리고 8분 자리를 우선한다
    돌려주는 값: 음절마다의 시각(초)"""
    g = grid_pos(times, song)
    out = np.round(g)
    lo, hi = song.g[0], song.g[-1]

    def cost(pos, k):
        h = hits[int(np.clip(k - lo, 0, len(hits) - 1))]
        return (pos - k) ** 2 + ODD_COST * ((k - home) % 2) - HIT_BONUS * h

    cuts = np.flatnonzero(np.diff(times) > PHRASE_GAP) + 1
    for ph in np.split(np.arange(len(times)), cuts):
        if not len(ph): continue
        shifts = np.arange(-0.3, 0.301, 0.1) if len(ph) >= 6 else [0.0]
        best = None
        for d in shifts:
            pos = g[ph] - d
            cand = [np.round(x) + np.array([-1, 0, 1]) for x in pos]
            # 동적 계획법: 앞에서부터, 직전 음절보다 뒤 칸이어야 한다 (0.6칸보다 가까이 붙은 두 음절은 같은 칸 허용)
            tot = np.array([cost(pos[0], k) for k in cand[0]])
            back = []
            for i in range(1, len(ph)):
                need = 1 if pos[i] - pos[i - 1] >= 0.6 else 0
                nt = np.full(3, np.inf); nb = np.zeros(3, dtype=int)
                for b, k in enumerate(cand[i]):
                    okp = cand[i - 1] + need <= k
                    if not okp.any(): continue
                    j = int(np.argmin(np.where(okp, tot, np.inf)))
                    nt[b] = tot[j] + cost(pos[i], k); nb[b] = j
                if not np.isfinite(nt).any():          # 막히면 제약 없이 가장 가까운 칸
                    nt = np.array([cost(pos[i], k) for k in cand[i]]) + tot.min(); nb[:] = int(np.argmin(tot))
                tot = nt; back.append(nb)
            j = int(np.argmin(tot))
            total = tot[j] + PHRASE_SHIFT * d * d
            if best is None or total < best[0]:
                ks = [cand[-1][j]]
                for i in range(len(ph) - 1, 0, -1):
                    j = back[i - 1][j]; ks.append(cand[i - 1][j])
                best = (total, ks[::-1])
        out[ph] = best[1]
    return grid_to_time(np.clip(out, lo, hi), song)


LAG_XS = np.arange(-1, 1, 0.02)      # 8분 자리에서 벗어난 정도 (16분 칸 단위)
LAG_HOME = 0.75                      # 곡 전체가 8분 자리에서 이보다 많이 벗어나 있으면, 가사가 홀수 칸에 놓이는 곡으로 본다


def _lag_density(g, sigma_slots):
    """음절의 격자 위치를 8분음표 주기로 접어 쌓은 밀도."""
    if not len(g): return np.ones(len(LAG_XS))
    d = (g[None, :] - LAG_XS[:, None] + 1) % 2 - 1
    return np.exp(-0.5 * (d / sigma_slots) ** 2).sum(1)


def _lag_path(t, g, song, win=24.0, step=6.0, jump=3.0):
    """24초 구간마다 밀도의 봉우리를 따라가되, 갑자기 튀지 않는 경로를 고른다 (동적 계획법)."""
    lo, hi = song.t[0], song.t[-1]
    cs = np.arange(lo + win / 2, max(hi - win / 2, lo + win / 2 + 1), step)
    S = []
    for c in cs:
        m = (t >= c - win / 2) & (t < c + win / 2)
        d = _lag_density(g[m], 0.016 / song.slot)
        S.append(d / d.mean() - 1 if m.sum() >= 20 else np.zeros(len(LAG_XS)))
    cost = jump * np.abs((LAG_XS[:, None] - LAG_XS[None, :] + 1) % 2 - 1)
    best, back = S[0].copy(), []
    for j in range(1, len(cs)):
        tot = best[None, :] - cost
        b = np.argmax(tot, axis=1)
        back.append(b)
        best = tot[np.arange(len(LAG_XS)), b] + S[j]
    k = int(np.argmax(best))
    path = [k]
    for b in back[::-1]:
        k = int(b[k]); path.append(k)
    return cs, np.unwrap(LAG_XS[np.array(path[::-1])] * np.pi) / np.pi


def _on_grid(g, lag, slot):
    """지연을 뺀 음절 중 16분 자리 ±25ms 안에 드는 비율(%)."""
    r = g - lag
    return float((np.abs(r - np.round(r)) < 0.025 / slot).mean() * 100)


def vocal_lag(times, song):
    """목소리가 반주 박자보다 늦는(+) / 빠른(-) 정도를 곡에서 잰다.
    부르는 사람의 버릇이나 녹음 지연 때문에 목소리 전체가 수십 ms 씩 밀려 있는 곡이 많은데,
    이것을 빼지 않고 박자에 붙이면 음절의 절반가량이 옆 칸으로 잘못 간다.
    세 가지(빼지 않음 / 곡 전체에 한 값 / 구간마다 따라감)를 음절의 절반으로 재고 나머지 절반으로 평가해서,
    복잡한 쪽이 2%p 이상 나을 때만 그쪽을 쓴다.
    돌려주는 값: (음절마다의 지연[초], 가사가 주로 놓이는 칸의 홀짝, 방식, 곡 전체 지연[초])"""
    m = (times > song.t[0]) & (times < song.t[-1])
    t, g = times[m], grid_pos(times[m], song)
    if len(t) < 80:
        return np.zeros(len(times)), 0, 'none', 0.0
    sig = 0.014 / song.slot
    score = dict(none=0.0, whole=0.0, track=0.0)
    idx = np.arange(len(t))
    for a in (0, 1):
        tr, te = idx % 2 == a, idx % 2 != a
        score['none'] += _on_grid(g[te], 0.0, song.slot) / 2
        score['whole'] += _on_grid(g[te], LAG_XS[np.argmax(_lag_density(g[tr], sig))], song.slot) / 2
        cs, path = _lag_path(t[tr], g[tr], song)
        score['track'] += _on_grid(g[te], np.interp(t[te], cs, path), song.slot) / 2
    mode = 'none'
    if score['whole'] >= score['none'] + 2: mode = 'whole'
    if score['track'] >= max(score['none'], score['whole']) + 2: mode = 'track'

    d = _lag_density(g, sig)
    x = float(LAG_XS[np.argmax(d)])
    home = int(abs(x) > LAG_HOME and d.max() / d.mean() >= 1.4)
    if mode == 'none':
        return np.zeros(len(times)), home, mode, 0.0
    if mode == 'whole':
        lag = np.full(len(times), x)
    else:
        cs, path = _lag_path(t, g, song)
        path = path + 2 * np.round((x - np.median(path)) / 2)
        lag = np.interp(times, cs, path)
    lag = lag - home * np.sign(x)
    return lag * song.slot, home, mode, float((x - home * np.sign(x)) * song.slot)


def refine_grid(song, drums):
    """드럼 타격을 기준으로 박자 격자의 미세한 밀림을 바로잡는다 (12초 구간마다 어긋남의 중간값을 재서 뺀다)."""
    pf, pw = A.pick_peaks(A.features(drums)[0], 1.0)
    if len(pf) < 50: return 0.0
    keep = pw >= np.quantile(pw, 0.6)
    p = A.frame_time(pf[keep])
    p = p[(p > song.t[0]) & (p < song.t[-1])]
    k = np.clip(np.searchsorted(song.t, p), 1, len(song.t) - 1)
    r = np.where(p - song.t[k - 1] < song.t[k] - p, p - song.t[k - 1], p - song.t[k])
    centers = np.arange(song.t[0], song.t[-1], 4.0)
    c = np.full(len(centers), np.nan)
    for j, tc in enumerate(centers):
        m = np.abs(p - tc) <= 6
        inl = m & (np.abs(r) < 0.25 * song.slot)
        if inl.sum() >= 15 and inl.sum() >= 0.6 * m.sum():
            c[j] = np.median(r[inl])
    good = ~np.isnan(c)
    if good.sum() < 3: return 0.0
    c = np.interp(centers, centers[good], c[good])
    c = np.convolve(np.pad(c, 1, mode='edge'), [0.25, 0.5, 0.25], mode='valid')
    song.t = song.t + np.interp(song.t, centers, c)
    return float(np.abs(c).max())


def dedupe(times, weights):
    """같은 자리에 겹친 것은 가장 센 것만 남긴다 (시간순 입력). 남길 번호를 돌려준다."""
    keep = []
    for i in range(len(times)):
        if keep and abs(times[i] - times[keep[-1]]) < 0.005:
            if weights[i] > weights[keep[-1]]: keep[-1] = i
        else:
            keep.append(i)
    return np.array(keep, dtype=int)


def thin(times, weights, gap):
    """센 것부터 고르되, 이미 고른 것과 gap 초 안에 있으면 버린다 (시간순 입력)."""
    taken = np.zeros(len(times), dtype=bool)
    for k in np.argsort(-weights, kind='stable'):
        lo = np.searchsorted(times, times[k] - gap, side='right')
        hi = np.searchsorted(times, times[k] + gap, side='left')
        if not taken[lo:hi].any(): taken[k] = True
    return np.flatnonzero(taken)


def generate(voc, mel, gaps, dur, keys, diff, seed, fills=None):
    P = DIFFS[diff]
    rng = random.Random(seed)
    events = []   # (시각, 길이, 음높이, 세기, 목소리인지)

    # ---- 가사 음절 ----
    ok = np.flatnonzero(voc.score >= P['floor'])
    picked = ok[thin(voc.t[ok], voc.score[ok], P['gap'])] if len(ok) else ok
    for j, k in enumerate(picked):
        t = voc.t[k]
        t_next = voc.t[picked[j + 1]] if j + 1 < len(picked) else dur
        # 다음 음절(고르지 않은 것 포함)이 오거나 목소리가 끊길 때까지가 이 음절의 길이
        hold = min(voc.end[k] if diff == 'Hard' else voc.run_end[k], t_next) - t
        ln = min(hold - 0.12, LONG_MAX) if hold >= LONG_MIN else 0.0
        events.append((float(t), float(ln), voc.pitch[k], float(voc.score[k]), True))

    # ---- 목소리가 쉬는 구간의 멜로디 ----
    mt, mw, mend, mpitch = mel
    for ga, gb in gaps:
        idx = np.flatnonzero((mt >= ga) & (mt <= gb))
        if not len(idx): continue
        target = max(1, int(P['mel_nps'] * (gb - ga)))
        sel = idx[thin(mt[idx], mw[idx], P['mel_gap'])]
        sel = sel[np.argsort(-mw[sel], kind='stable')[:target]]
        sel.sort()
        for j, k in enumerate(sel):
            t_next = mt[sel[j + 1]] if j + 1 < len(sel) else gb
            hold = min(mend[k], t_next) - mt[k]
            ln = min(hold - 0.12, LONG_MAX) if hold >= LONG_MIN else 0.0
            events.append((float(mt[k]), float(ln), mpitch[k], float(mw[k]), False))

    # ---- (Very Hard 이상) 가사 구절 안에서 반주가 치는 박자에 노트를 더 넣는다 ----
    if P.get('fill') and fills is not None and events:
        ft, fh, feven = fills
        vt = np.array(sorted(e[0] for e in events if e[4]))
        taken = np.array(sorted(e[0] for e in events))
        cand = []
        for t, h, ev in zip(ft, fh, feven):
            if h < P['fill_hit'] or (P['fill_even'] and not ev): continue
            i = np.searchsorted(vt, t)
            if i == 0 or i == len(vt) or t - vt[i - 1] > 1.2 or vt[i] - t > 1.2: continue   # 구절 밖(전주·간주)은 건너뛴다
            cand.append((h, t))
        cand.sort(reverse=True)
        limit = int(P['fill'] * len(vt))
        added = []
        for h, t in cand:
            if len(added) >= limit: break
            i = np.searchsorted(taken, t)
            if i > 0 and t - taken[i - 1] < P['fill_gap']: continue
            if i < len(taken) and taken[i] - t < P['fill_gap']: continue
            taken = np.insert(taken, i, t)
            added.append((float(t), 0.0, None, float(h), False))
        events += added

    events.sort(key=lambda e: e[0])
    events = [e for e in events if 1.0 <= e[0] <= dur - 1.0]
    strengths = np.array([e[3] for e in events if e[4]]) if events else np.zeros(0)
    chord_thr = np.quantile(strengths, 1 - P['chord']) if P['chord'] > 0 and len(strengths) else 1e9

    notes = []
    busy = [-1.0] * keys
    last_hit = [-9.0] * keys
    lane, prev_st, prev_vocal = rng.randrange(keys), None, None
    for t, ln, st, w, is_vocal in events:
        free = [l for l in range(keys) if busy[l] < t - 0.02]
        if not free: continue
        if is_vocal != prev_vocal: prev_st = None            # 목소리 ↔ 멜로디가 바뀌면 음높이 비교를 새로 시작
        # 음이 올라가면 오른쪽, 내려가면 왼쪽, 같은 음이면 같은 자리
        if st is None or prev_st is None: step = rng.choice([-1, 1])
        else:
            d = st - prev_st
            step = 0 if abs(d) < 0.6 else (2 if abs(d) >= 5 else 1) * (1 if d > 0 else -1)
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

        if ln == 0 and is_vocal and w >= chord_thr:
            free2 = [m for m in free if m != l and t - last_hit[m] >= P['jack']]
            if free2:
                mirror = keys - 1 - l
                m = mirror if mirror in free2 else rng.choice(free2)
                notes.append(dict(t=int(round(t * 1000)), lane=m))
                busy[m] = t; last_hit[m] = t
        lane, prev_vocal = l, is_vocal
        if st is not None: prev_st = st

    notes.sort(key=lambda n: (n['t'], n['lane']))
    span = max(1.0, (notes[-1]['t'] - notes[0]['t']) / 1000) if notes else 1.0
    level = int(min(30, max(1, round(len(notes) / span * 3.2 + (1 if keys == 6 else 0)))))
    return dict(keys=keys, difficulty=diff, level=level, notes=notes)


def process(songs_dir, stems_dir, title):
    json_path = os.path.join(songs_dir, title, 'song.json')
    stem_path = os.path.join(stems_dir, title + '.npz')
    if not (os.path.exists(json_path) and os.path.exists(stem_path)):
        return
    z = np.load(stem_path)
    with open(json_path, encoding='utf-8') as f:
        data = json.load(f)

    voc = Vocal(z['vocals'].astype(np.float32))
    track = M.Track(z['other'].astype(np.float32))
    dur = track.dur
    gaps = voc.melody_gaps()

    # 원곡 박자 격자: 전체 음원 / 반주 / 멜로디 악기 중 박자가 가장 또렷하게 잡히는 쪽을 쓴다.
    # (목소리가 박자에서 벗어나 있으면 전체 음원으로는 박자를 못 찾는 곡이 있다)
    mix = A.decode(os.path.join(songs_dir, title, data['audio']))
    song = max((A.Song(sig) for sig in (mix, z['inst'].astype(np.float32), z['other'].astype(np.float32))),
               key=lambda g: g.quality['within25'])
    quality = song.quality['within25']
    data['bpm'] = round(song.bpm, 3)

    mt = track.t.copy()
    fills = None
    lag_ms, home, mode, fixed = 0.0, 0, 'none', 0.0
    if quality >= SNAP_MIN_QUALITY:
        n = min(len(mix), len(z['vocals']), len(z['inst']))
        drums = mix[:n] - z['vocals'][:n].astype(np.float32) - z['inst'][:n].astype(np.float32)
        fixed = refine_grid(song, drums) * 1000
        # 모든 노트를 박자에 붙인다. 부르는 사람의 음절은 박자에서 수십 ms 씩 들쭉날쭉하므로,
        # 그대로 두면 어떤 노트는 빠르고 어떤 노트는 느리게 느껴진다.
        lag, home, mode, lag0 = vocal_lag(voc.t, song)
        lag_ms = lag0 * 1000
        hits = slot_hits(song, mix[:n] - z['vocals'][:n].astype(np.float32))
        fills = (song.t.copy(), hits, (song.g - home) % 2 == 0)
        new_t = align_vocal(voc.t - lag, song, home, hits)
        shift = new_t - voc.t
        voc.t, voc.end, voc.run_end = new_t, voc.end + shift, voc.run_end + shift
        keep = dedupe(voc.t, voc.score)
        voc.t, voc.score, voc.end, voc.run_end = voc.t[keep], voc.score[keep], voc.end[keep], voc.run_end[keep]
        voc.kind = [voc.kind[i] for i in keep]; voc.pitch = [voc.pitch[i] for i in keep]
        mt = snap_grid(track.t, song)
    order = np.argsort(mt, kind='stable')
    order = order[dedupe(mt[order], track.w[order])]
    mel = (mt[order], track.w[order], (track.end + (mt - track.t))[order], [track.pitch[i] for i in order])

    seed = zlib.crc32(title.encode('utf-8'))
    data['charts'] = [generate(voc, mel, gaps, dur, k, d, seed + k * 10 + n, fills) for k in (4, 6) for n, d in enumerate(DIFFS)]
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, separators=(',', ':'))

    kinds = {k: voc.kind.count(k) for k in ('start', 'dip', 'pitch')}
    c4 = data['charts'][:3]
    gap_sec = sum(b - a for a, b in gaps)
    print('%-22s 박자 %7.3f (%2.0f%%, 드럼 보정 %2.0fms)%s | 목소리 지연 %+4.0fms %-5s%s | 음절 %4d | 간주 %3.0f초 | 4K E=%d N=%d H=%d 롱 %d' %
          (title, song.bpm, quality, fixed, '' if quality >= SNAP_MIN_QUALITY else ' 붙이지 않음', lag_ms, mode, ' 홀수칸' if home else '', len(voc.t),
           gap_sec, len(c4[0]['notes']), len(c4[1]['notes']), len(c4[2]['notes']),
           sum(1 for n in c4[1]['notes'] if n.get('len'))), flush=True)


if __name__ == '__main__':
    songs_dir, stems_dir = sys.argv[1], sys.argv[2]
    A.calibrate()
    bias, spread = calibrate_vocal()
    print('음절 검출 보정 %.1fms (흔들림 %.1fms)' % (bias * 1000, spread * 1000), flush=True)
    for title in (sys.argv[3:] or sorted(os.listdir(songs_dir))):
        try:
            process(songs_dir, stems_dir, title)
        except Exception as e:
            import traceback; traceback.print_exc()
            print('실패: %s (%s)' % (title, e), flush=True)
