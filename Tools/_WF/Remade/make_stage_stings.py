"""Mixes the stage stings a Monolith plays to the whole sector when it climbs.

Each sting is a reversed, echoing swell of a tgstation curse sound into a hit of the same curse, the DS13 marker throb and a
ghost whisper, over a slowed tgstation "signal" drone that fades out. Higher stages are lower, longer and louder.

Usage: python Tools/_WF/Remade/make_stage_stings.py <tgstation checkout>
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
AUDIO = ROOT / "Resources/Audio/_WF/Remade"
OUT = AUDIO / "monolith"

# stage name, curse file, whisper, pitch, tail seconds, gain
STAGES = [
    ("hymn", "curse1.ogg", "whisper_hit_1.ogg", 0.85, 5.0, 0.7),
    ("growth", "curse3.ogg", "whisper_hit_2.ogg", 0.78, 6.0, 0.85),
    ("harvest", "curse5.ogg", "whisper_hit_3.ogg", 0.7, 7.0, 1.0),
    ("rapture", "curseattack.ogg", "whisper_hit_4.ogg", 0.6, 8.0, 1.15),
]


def run(args):
    subprocess.run(["ffmpeg", "-v", "error", "-y", *args], check=True)


def duration(path):
    out = subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
        check=True, capture_output=True, text=True)
    return float(out.stdout.strip())


def main():
    tg = Path(sys.argv[1]) / "sound"
    signal = tg / "ambience/misc/signal.ogg"
    throb = AUDIO / "monolith/throb.ogg"
    tmp = OUT / "_swell.wav"

    for name, curse, whisper, pitch, tail, gain in STAGES:
        source = tg / "effects/curse" / curse
        # The swell: the curse slowed, smeared with echo and played backwards, so it sucks in towards the hit.
        run(["-i", str(source), "-af",
             f"aresample=44100,asetrate={44100 * pitch * 0.85:.0f},aresample=44100,apad=pad_dur=2,"
             "aecho=0.8:0.9:400|900|1500:0.5|0.35|0.2,areverse,afade=t=in:d=1.2,volume=0.9",
             "-ac", "2", str(tmp)])
        delay = int(duration(tmp) * 1000) - 80
        graph = (
            "[0]anull[sw];"
            f"[1]aresample=44100,asetrate={44100 * pitch:.0f},aresample=44100,apad=pad_dur=1.5,"
            f"aecho=0.8:0.8:300|700:0.4|0.25,adelay={delay}|{delay},volume=1.1[hit];"
            f"[2]aresample=44100,asetrate={44100 * pitch * 1.1:.0f},aresample=44100,adelay={delay}|{delay},volume=0.9[th];"
            f"[3]aresample=44100,asetrate={44100 * min(1.0, pitch * 1.15):.0f},aresample=44100,"
            f"adelay={delay + 150}|{delay + 150},volume=0.6[wh];"
            f"[4]aresample=44100,asetrate={44100 * pitch * 0.7:.0f},aresample=44100,lowpass=f=1600,"
            f"atrim=0:{tail + 2},afade=t=in:d=0.4,afade=t=out:st={tail - 2}:d=3,adelay={delay}|{delay},volume=0.8[tl];"
            f"[sw][hit][th][wh][tl]amix=inputs=5:normalize=0:duration=longest,"
            f"atrim=0:{delay / 1000 + tail + 1},afade=t=out:st={delay / 1000 + tail - 1}:d=2,"
            f"alimiter=limit=0.9,volume={gain}[out]"
        )
        run(["-i", str(tmp), "-i", str(source), "-i", str(throb), "-i", str(AUDIO / "dread" / whisper), "-i", str(signal),
             "-filter_complex", graph, "-map", "[out]", "-ac", "2", "-c:a", "libvorbis", "-q:a", "4",
             str(OUT / f"stage_{name}.ogg")])
        print(f"stage_{name}.ogg {duration(OUT / f'stage_{name}.ogg'):.1f} s")

    tmp.unlink()


if __name__ == "__main__":
    main()
