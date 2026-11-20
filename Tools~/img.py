"""PNG 읽기/쓰기 (ffmpeg 사용). load(path) -> HxWx4 uint8, save(path, array)."""
import subprocess, re
import numpy as np
import imageio_ffmpeg
FF = imageio_ffmpeg.get_ffmpeg_exe()

def load(path):
    info = subprocess.run([FF, '-hide_banner', '-i', path], capture_output=True).stderr.decode('utf-8', 'replace')
    m = re.search(r'Video:.*?(\d{2,5})x(\d{2,5})', info)
    w, h = int(m.group(1)), int(m.group(2))
    raw = subprocess.run([FF, '-hide_banner', '-loglevel', 'error', '-i', path, '-f', 'rawvideo', '-pix_fmt', 'rgba', '-'], check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.uint8).reshape(h, w, 4).copy()

def save(path, a):
    h, w = a.shape[:2]
    subprocess.run([FF, '-hide_banner', '-loglevel', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgba', '-s', '%dx%d' % (w, h), '-i', '-',
                    '-frames:v', '1', '-update', '1', path], input=np.ascontiguousarray(a).tobytes(), check=True)
