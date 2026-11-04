#!/usr/bin/env python3
"""Ports DS13's music and the small sounds the Remade betrayal effects use into `Resources/Audio/_WF/Remade/betrayal`.

    python Tools/_WF/Remade/port_betrayal.py [--ds13-2 C:/Users/jzo12/Documents/GitHub/DS13-2.0] [--ds13-1 C:/Users/jzo12/Documents/GitHub/DS13-1.0]

The seven DS13-2.0 music tracks are re-encoded as stereo Vorbis at a low bitrate (the credits tracks ship at 500 kbps). Four
stingers are cut from them at their sharpest onsets. Knocks are DS13-1.0's thuds and bang run through a low-pass, as if heard
through a door; scrapes are its metal scrapes; steps are DS13-2.0's plating and floor steps; breaths are synthesised here with
ffmpeg (DS13 has none). Everything but music is mono. Reruns overwrite the output and rewrite the generated block of
`attributions.yml`. Needs ffmpeg with libvorbis. port_assets.py wipes the whole Remade audio folder: rerun this after it.
"""
from __future__ import annotations

import argparse
import array
import subprocess
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / "Resources/Audio/_WF/Remade/betrayal"
ATTRIBUTIONS = OUT / "attributions.yml"
MARK_START = "# GENERATED START: Tools/_WF/Remade/port_betrayal.py\n"
MARK_END = "# GENERATED END\n"

URL_2 = "https://github.com/DS-13-Dev-Team/DS13-2.0"
URL_1 = "https://github.com/DS-13-Dev-Team/DS13-1.0"

MUSIC = ["credits_rock", "credits_violin", "danik_speech", "nicole", "pensive", "unitology"]
STINGER_SOURCES = ["unitology", "pensive", "nicole"]
STINGER_LENGTH = 5.5


def run(args: list[str]) -> None:
    subprocess.run(["ffmpeg", "-v", "error", "-y", *args], check=True)


def onset(src: Path) -> float:
    """The time in seconds of the sharpest rise in loudness, which a stinger starts just before."""
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(src), "-ac", "1", "-ar", "4000", "-f", "s16le", "-"],
                         capture_output=True, check=True).stdout
    samples = array.array("h")
    samples.frombytes(raw[: len(raw) // 2 * 2])
    step = 2000
    rms = [(sum(s * s for s in samples[i:i + step]) / step) ** 0.5 for i in range(0, len(samples) - step, step)]
    best, best_time = 0.0, 0.0
    for i in range(4, len(rms) - 1):
        before = sum(rms[i - 4:i]) / 4 + 50
        rise = rms[i] / before
        if rise > best and i * 0.5 < len(rms) * 0.5 - STINGER_LENGTH:
            best, best_time = rise, i * 0.5
    return max(best_time - 0.3, 0.0)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ds13-2", default="C:/Users/jzo12/Documents/GitHub/DS13-2.0")
    parser.add_argument("--ds13-1", default="C:/Users/jzo12/Documents/GitHub/DS13-1.0")
    args = parser.parse_args()

    ds2 = Path(args.ds13_2) / "deadspace/sound"
    ds1 = Path(args.ds13_1) / "sound"
    OUT.mkdir(parents=True, exist_ok=True)
    entries: list[tuple[str, str, str]] = []

    def add(name: str, how: str, url: str) -> None:
        entries.append((name, how, url))

    for name in MUSIC:
        run(["-i", str(ds2 / "music/ds13" / f"{name}.ogg"), "-c:a", "libvorbis", "-q:a", "1", str(OUT / f"music_{name}.ogg")])
        add(f"music_{name}.ogg", f"Taken from DS13-2.0 sound/music/ds13/{name}.ogg, re-encoded at a lower bitrate", URL_2)

    for i, name in enumerate(STINGER_SOURCES, 1):
        start = onset(ds2 / "music/ds13" / f"{name}.ogg")
        fade = f"afade=t=in:d=0.03,afade=t=out:st={STINGER_LENGTH - 2.0}:d=2.0"
        run(["-ss", f"{start:.2f}", "-t", str(STINGER_LENGTH), "-i", str(ds2 / "music/ds13" / f"{name}.ogg"), "-af", fade,
             "-c:a", "libvorbis", "-q:a", "3", str(OUT / f"stinger_{i}.ogg")])
        add(f"stinger_{i}.ogg", f"Cut from DS13-2.0 sound/music/ds13/{name}.ogg at {start:.1f} s", URL_2)

    # Knocks: dull thuds, muffled as if from behind a door.
    knocks = [(ds1 / f"effects/impacts/thud{n}.ogg", f"effects/impacts/thud{n}.ogg") for n in (1, 2, 3)]
    knocks.append((ds1 / "effects/bang.ogg", "effects/bang.ogg"))
    for i, (src, rel) in enumerate(knocks, 1):
        run(["-i", str(src), "-af", "lowpass=f=700,volume=1.6,alimiter=limit=0.9", "-ac", "1", "-c:a", "libvorbis", "-q:a", "4",
             str(OUT / f"knock_{i}.ogg")])
        add(f"knock_{i}.ogg", f"Taken from DS13-1.0 sound/{rel}, low-passed and downmixed to mono", URL_1)

    for n in (1, 2, 3):
        run(["-i", str(ds1 / f"effects/metalscrape{n}.ogg"), "-af", "lowpass=f=3200,volume=0.8", "-ac", "1", "-c:a", "libvorbis",
             "-q:a", "4", str(OUT / f"scrape_{n}.ogg")])
        add(f"scrape_{n}.ogg", f"Taken from DS13-1.0 sound/effects/metalscrape{n}.ogg, low-passed and downmixed to mono", URL_1)

    steps = [f"footstep/plating{n}.ogg" for n in range(1, 6)] + [f"footstep/floor{n}.ogg" for n in range(1, 6)]
    for i, rel in enumerate(steps, 1):
        run(["-i", str(ds2 / "effects" / rel), "-ac", "1", "-c:a", "libvorbis", "-q:a", "4", str(OUT / f"step_{i}.ogg")])
        add(f"step_{i}.ogg", f"Taken from DS13-2.0 sound/effects/{rel}, downmixed to mono", URL_2)

    # Breaths: DS13 has none, so they are filtered pink noise under an inhale and exhale envelope.
    breaths = [(2.6, 350, 2400), (3.0, 250, 1800), (2.2, 450, 3000)]
    for i, (length, low, high) in enumerate(breaths, 1):
        env = "if(lt(t,0.9),pow(t/0.9,2),if(lt(t,1.1),0.04,exp(-(t-1.1)*2.2)))"
        run(["-f", "lavfi", "-i", f"anoisesrc=d={length}:c=pink:r=44100:a=0.6", "-af",
             f"highpass=f={low},lowpass=f={high},volume='{env}':eval=frame,afade=t=out:st={length - 0.2}:d=0.2",
             "-ac", "1", "-c:a", "libvorbis", "-q:a", "4", str(OUT / f"breath_{i}.ogg")])
        add(f"breath_{i}.ogg", "Synthesised for Wolfgate with ffmpeg anoisesrc (pink noise, filtered, with a breath envelope)", "https://github.com/VanguardControl/Wolfgate")

    block = [MARK_START]
    for name, how, url in entries:
        licence = "CC-BY-SA-3.0"
        block.append(f'- files: ["{name}"]\n  license: "{licence}"\n  copyright: "{how}"\n  source: "{url}"\n\n')
    block[-1] = block[-1].rstrip("\n") + "\n"
    block.append(MARK_END)

    text = ATTRIBUTIONS.read_text(encoding="utf-8") if ATTRIBUTIONS.exists() else ""
    if MARK_START in text:
        head, rest = text.split(MARK_START, 1)
        tail = rest.split(MARK_END, 1)[1] if MARK_END in rest else ""
        text = head.rstrip("\n") + "\n\n" + "".join(block) + tail
    else:
        text = text.rstrip("\n") + "\n\n" + "".join(block) if text else "".join(block)

    ATTRIBUTIONS.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {len(entries)} files to {OUT}")


if __name__ == "__main__":
    main()
