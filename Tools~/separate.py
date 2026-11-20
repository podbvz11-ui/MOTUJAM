"""
Demucs로 곡에서 보컬과 반주를 분리해 분석용 파일(.npz)로 저장한다.

사용법 (Tools~/venv 의 파이썬으로 실행):
    venv/Scripts/python separate.py <Songs 폴더> <stems 폴더>

<Songs 폴더>/<곡>/audio.ogg 를 읽어 <stems 폴더>/<곡>.npz 를 만든다.
  vocals : 보컬 (모노, 22050Hz)
  inst   : 드럼을 뺀 반주 = 베이스 + 그 밖의 악기 (모노, 22050Hz)
  other  : 멜로디·화음 악기 (드럼·베이스·보컬 제외)
  bass   : 베이스
이미 있는 곡은 건너뛴다.
"""
import sys, os, subprocess
import numpy as np
import torch
import torchaudio
import imageio_ffmpeg
from demucs.pretrained import get_model
from demucs.apply import apply_model

FF = imageio_ffmpeg.get_ffmpeg_exe()
OUT_SR = 22050


def decode_stereo(path, sr):
    out = subprocess.run([FF, '-hide_banner', '-loglevel', 'error', '-i', path, '-vn', '-ac', '2', '-ar', str(sr), '-f', 'f32le', '-'],
                         check=True, capture_output=True).stdout
    return torch.from_numpy(np.frombuffer(out, dtype=np.float32).reshape(-1, 2).T.copy())


def main():
    songs_dir, stems_dir = sys.argv[1], sys.argv[2]
    os.makedirs(stems_dir, exist_ok=True)
    device = 'cuda' if torch.cuda.is_available() else 'cpu'
    model = get_model('htdemucs')
    model.to(device).eval()
    print('device:', device, 'sources:', model.sources, flush=True)
    idx = {name: i for i, name in enumerate(model.sources)}

    for title in sorted(os.listdir(songs_dir)):
        audio = os.path.join(songs_dir, title, 'audio.ogg')
        out = os.path.join(stems_dir, title + '.npz')
        if not os.path.exists(audio):
            continue
        if os.path.exists(out):
            with np.load(out) as old:
                if 'other' in old.files:
                    continue
        try:
            wav = decode_stereo(audio, model.samplerate)
            ref = wav.mean(0)
            mean, std = ref.mean(), ref.std() + 1e-8
            with torch.no_grad():
                est = apply_model(model, ((wav - mean) / std)[None], device=device, split=True, overlap=0.25, progress=False)[0]
            est = est.cpu() * std + mean

            def mono(x):
                return torchaudio.functional.resample(x.mean(0), model.samplerate, OUT_SR).numpy().astype(np.float16)

            np.savez_compressed(out, vocals=mono(est[idx['vocals']]), inst=mono(est[idx['other']] + est[idx['bass']]),
                                other=mono(est[idx['other']]), bass=mono(est[idx['bass']]))
            print('ok', title, flush=True)
        except Exception as e:
            print('FAIL', title, e, flush=True)
        if device == 'cuda':
            torch.cuda.empty_cache()


if __name__ == '__main__':
    main()
