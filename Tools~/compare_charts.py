"""백업한 채보와 새 채보를 비교한다: 노트 수, 같은 자리에 남은 비율, 옮겨진 거리, 격자 정합."""
import sys, os, json
import numpy as np
songs, backup = sys.argv[1], sys.argv[2]
tot = [0, 0, 0]; bad = 0
for f in sorted(os.listdir(backup)):
    name = f[:-5]
    p = os.path.join(songs, name, 'song.json')
    if not os.path.exists(p): continue
    old = json.load(open(os.path.join(backup, f), encoding='utf-8')); new = json.load(open(p, encoding='utf-8'))
    if old == new: continue
    slot = 15000 / new['bpm']
    row = []
    for co, cn in zip(old['charts'], new['charts']):
        to = np.unique([n['t'] for n in co['notes']]); tn = np.unique([n['t'] for n in cn['notes']])
        k = np.clip(np.searchsorted(to, tn), 1, len(to) - 1)
        d = np.minimum(np.abs(tn - to[k - 1]), np.abs(tn - to[k]))
        same = (d <= 12).mean() * 100; one = ((d > 12) & (d < 1.5 * slot)).mean() * 100
        # 겹침/레인 검사
        last = {}
        for n in sorted(cn['notes'], key=lambda n: n['t']):
            if not 0 <= n['lane'] < cn['keys'] or n['t'] < last.get(n['lane'], -1): bad += 1
            last[n['lane']] = n['t'] + n.get('len', 0)
        iv = np.diff(tn) / slot
        ongrid = (np.abs(iv - np.round(iv)) < 0.08).mean() * 100
        if cn['keys'] == 4: row.append('%s %d→%d 그대로 %2.0f%% 옆칸 %2.0f%% 격자 %3.0f%%' % (cn['difficulty'][0], len(co['notes']), len(cn['notes']), same, one, ongrid))
        tot[0] += len(co['notes']); tot[1] += len(cn['notes']); tot[2] += 1
    print('%-18s %s' % (name[:18], ' | '.join(row)))
print('채보 %d개, 노트 %d → %d, 문제 %d' % (tot[2], tot[0], tot[1], bad))
