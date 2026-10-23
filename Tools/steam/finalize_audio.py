# 仕上げの音(2026-10-23): AAC にすると真のピークが WAV より 1〜2.5dB 上がる。
# 完成した mp4 の映像はそのまま(-c:v copy)、音だけを最後の WAV から作り直し、AAC の後の真のピークが目標以下になるまで少しずつ下げる。
#   python finalize_audio.py <in.mp4> <mix.wav> <out.mp4> [target_tp=-1.2]
import subprocess, sys, os

FF = os.environ.get("FFMPEG", "ffmpeg")


def measure(path):
    r = subprocess.run([FF, "-hide_banner", "-i", path, "-af", "ebur128=framelog=quiet:peak=true", "-f", "null", "-"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    t = r.stderr[r.stderr.rindex("Summary:"):]
    return float(t.split("I:")[1].split("LUFS")[0]), float(t.split("Peak:")[1].split("dBFS")[0])


def main():
    src, wav, out = sys.argv[1], sys.argv[2], sys.argv[3]
    target = float(sys.argv[4]) if len(sys.argv) > 4 else -1.2
    tmp = out + ".audio.m4a"
    # 全体の音量はそのまま、ピークだけを抑えるリミッター(4倍の細かさで見る)の上限を少しずつ下げる。
    # AAC のピークは音量に比例しない(一部の音で大きく跳ねる)ので、音量を下げるより確実で、全体の大きさもほぼ変わらない
    best = None
    for ceil in [-2.0, -2.5, -3.0, -3.5, -4.0, -4.5, -5.0, -6.0]:
        lim = 10 ** (ceil / 20.0)
        af = f"aresample=192000,alimiter=limit={lim:.4f}:attack=0.5:release=40:level=false,aresample=48000"
        subprocess.run([FF, "-y", "-loglevel", "error", "-i", wav, "-af", af, "-c:a", "aac", "-b:a", "256k", "-ar", "48000", tmp], check=True)
        i, tp = measure(tmp)
        print(f"ceiling {ceil:.1f} dB -> {i:.2f} LUFS, true peak {tp:.2f} dBTP")
        if tp <= target:
            best = ceil
            break
    if best is None:
        sys.exit("true peak target not reached")
    subprocess.run([FF, "-y", "-loglevel", "error", "-i", src, "-i", tmp, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "copy",
                    "-movflags", "+faststart", "-shortest", out], check=True)
    os.remove(tmp)
    i, tp = measure(out)
    print(f"final {out}: {i:.2f} LUFS, true peak {tp:.2f} dBTP")


if __name__ == "__main__":
    main()
