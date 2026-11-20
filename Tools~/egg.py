"""EGG(알집) 압축 파일 목록 보기 / 풀기.  사용법: egg.py list <egg>  |  egg.py extract <egg> <폴더>"""
import struct, sys, os, zlib, bz2

END = 0x08E28222

def read_entries(f):
    """(name, size, [(method, usize, csize, data_offset)]) 를 차례로 돌려준다."""
    magic, ver, hid, res = struct.unpack('<IHII', f.read(14))
    assert magic == 0x41474745, 'EGG 파일이 아닙니다'
    cur = None
    while True:
        b = f.read(4)
        if len(b) < 4:
            break
        m = struct.unpack('<I', b)[0]
        if m == END:
            continue
        if m == 0x0A8590E3:                      # 파일 헤더
            if cur: yield cur
            fid, length = struct.unpack('<IQ', f.read(12))
            cur = [None, length, []]
        elif m == 0x0A8591AC:                    # 파일 이름
            flag, size = struct.unpack('<BH', f.read(3))
            if flag & 0x08: f.read(2)
            if flag & 0x10: f.read(4)
            raw = f.read(size)
            try: cur[0] = raw.decode('utf-8')
            except UnicodeDecodeError: cur[0] = raw.decode('cp949', 'replace')
        elif m in (0x04C63672, 0x2C86950B, 0x1EE922E5, 0x08D1470F, 0x24F5A262, 0x24E5A060, 0x07463307):
            flag, size = struct.unpack('<BH', f.read(3))
            if m == 0x08D1470F: raise SystemExit('암호가 걸린 파일은 지원하지 않습니다')
            f.read(size)
        elif m == 0x02B50C13:                    # 블록 헤더
            method, hint, usize, csize, crc = struct.unpack('<BBIII', f.read(14))
            end = struct.unpack('<I', f.read(4))[0]
            assert end == END
            cur[2].append((method, usize, csize, f.tell()))
            f.seek(csize, 1)
        else:
            raise SystemExit('알 수 없는 헤더 0x%08X @ %d' % (m, f.tell() - 4))
    if cur: yield cur

def extract(f, entry, dest):
    name, size, blocks = entry
    safe = os.path.basename(name.replace(chr(92), '/'))      # 폴더 밖으로 못 나가게 파일명만 사용
    out = os.path.join(dest, safe)
    with open(out, 'wb') as o:
        for method, usize, csize, off in blocks:
            f.seek(off)
            if method == 0:
                left = csize
                while left:
                    chunk = f.read(min(left, 1 << 24)); o.write(chunk); left -= len(chunk)
            elif method in (1, 2):
                d = zlib.decompressobj(-15) if method == 1 else bz2.BZ2Decompressor()
                left = csize
                while left:
                    chunk = f.read(min(left, 1 << 24)); o.write(d.decompress(chunk)); left -= len(chunk)
                if method == 1: o.write(d.flush())
            else:
                raise SystemExit('지원하지 않는 압축 방식 %d: %s' % (method, name))
    return out

if __name__ == '__main__':
    cmd, path = sys.argv[1], sys.argv[2]
    with open(path, 'rb') as f:
        entries = list(read_entries(f))
        for e in entries:
            methods = sorted(set(b[0] for b in e[2]))
            print('%10.1f MB  methods=%s  %s' % (e[1] / 1e6, methods, e[0]))
        if cmd == 'extract':
            dest = sys.argv[3]; os.makedirs(dest, exist_ok=True)
            for e in entries:
                print('extract', extract(f, e, dest), flush=True)
