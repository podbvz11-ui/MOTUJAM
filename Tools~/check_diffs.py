"""난이도별 노트 수·초당 노트·레벨을 보여 주고, 기존 난이도가 백업과 같은지와 채보 오류를 검사한다."""
import sys, os, json
import numpy as np
songs, backup = sys.argv[1], sys.argv[2]
only = sys.argv[3:]
bad = changed = total = 0
for f in sorted(os.listdir(backup)):
    name = f[:-5]
    if only and name not in only: continue
    new = json.load(open(os.path.join(songs, name, 'song.json'), encoding='utf-8'))
    old = {(c['keys'], c['difficulty']): c for c in json.load(open(os.path.join(backup, f), encoding='utf-8'))['charts']}
    slot = 15000 / new['bpm']; row = []
    for c in new['charts']:
        total += 1
        key = (c['keys'], c['difficulty'])
        if key in old and old[key]['notes'] != c['notes']: changed += 1
        last = {}
        for n in sorted(c['notes'], key=lambda n: n['t']):
            if not 0 <= n['lane'] < c['keys'] or n['t'] <= last.get(n['lane'], -1): bad += 1
            last[n['lane']] = n['t'] + n.get('len', 0)
        t = np.array(sorted(n['t'] for n in c['notes']))
        iv = np.diff(np.unique(t)) / slot
        off = (np.abs(iv - np.round(iv)) > 0.08).sum()
        span = (t[-1] - t[0]) / 1000
        # 가장 바쁜 5초 구간의 초당 노트
        peak = max(np.searchsorted(t, x + 5000) - i for i, x in enumerate(t)) / 5
        if c['keys'] == 4: row.append('%s %d개 %.1f/초(최대 %.1f) Lv%d%s' % (c['difficulty'][:2], len(t), len(t) / span, peak, c['level'], ' 격자밖%d' % off if off else ''))
    print('%-14s %s' % (name[:14], ' | '.join(row)))
print('채보 %d개, 기존 난이도 중 달라진 것 %d개, 오류 %d' % (total, changed, bad))
