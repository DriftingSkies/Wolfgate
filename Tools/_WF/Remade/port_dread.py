#!/usr/bin/env python3
"""Cuts the Remade dread whispers and the whisper bed out of DS13's recordings.

    python Tools/_WF/Remade/port_dread.py [--ds13-2 C:/Users/jzo12/Documents/GitHub/DS13-2.0] [--ds13-1 C:/Users/jzo12/Documents/GitHub/DS13-1.0]

DS13 has one real whisper recording, `sound/voice/ghost_whisper.ogg`, a layered murmur of many voices. Every file made here is
that recording cut at its quiet points, slowed, played backwards or thinned, and every file is mono. The server plays the cuts
as positional whispers (`WFDreadWhispers`); the client loops the long ones as a bed under the stone's influence. The older
voice and menace files in `Resources/Audio/_WF/Remade/dread` were copied by hand and are not touched. Needs ffmpeg with libvorbis.
Reruns overwrite the output and rewrite the generated block of `attributions.yml`.
"""
from __future__ import annotations

import argparse
import re
import subprocess
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
DREAD = REPO / "Resources/Audio/_WF/Remade/dread"
ATTRIBUTIONS = DREAD / "attributions.yml"
MARK_START = "# GENERATED START: Tools/_WF/Remade/port_dread.py\n"
MARK_END = "# GENERATED END\n"

URL_2 = "https://github.com/DS-13-Dev-Team/DS13-2.0"
URL_1 = "https://github.com/DS-13-Dev-Team/DS13-1.0"

# Quiet points in ghost_whisper.ogg, in seconds, where it can be cut without a click.
CUTS = [(0.0, 5.6), (5.6, 8.4), (8.4, 13.1), (13.1, 17.28)]


TARGET_DB = -23.0


def mean_db(src: Path, chain: str) -> float:
    """The mean level of the source after the filter chain, as ffmpeg's volumedetect reports it."""
    result = subprocess.run(["ffmpeg", "-v", "info", "-i", str(src), "-af", f"{chain},volumedetect", "-f", "null", "-"],
                            capture_output=True, text=True, check=True)
    match = re.search(r"mean_volume: (-?[\d.]+) dB", result.stderr)
    return float(match.group(1)) if match else TARGET_DB


def encode(src: Path, dest: Path, chain: str, quality: int = 4, level: bool = True) -> None:
    """Runs the chain, brings the result to a common level so the cuts sound as loud as one another, and writes mono Vorbis."""
    if level:
        chain = f"{chain},volume={TARGET_DB - mean_db(src, chain):.1f}dB,alimiter=limit=0.9"

    command = ["ffmpeg", "-v", "error", "-y", "-i", str(src), "-af", chain, "-ac", "1", "-c:a", "libvorbis", "-q:a", str(quality), str(dest)]
    subprocess.run(command, check=True)


def fades(length: float, fade: float = 0.06) -> str:
    return f"afade=t=in:d={fade},afade=t=out:st={max(length - fade, 0):.3f}:d={fade}"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ds13-2", default="C:/Users/jzo12/Documents/GitHub/DS13-2.0")
    parser.add_argument("--ds13-1", default="C:/Users/jzo12/Documents/GitHub/DS13-1.0")
    args = parser.parse_args()

    whisper = Path(args.ds13_2) / "sound/voice/ghost_whisper.ogg"
    drone = Path(args.ds13_1) / "sound/ambience/spookyspace1.ogg"
    DREAD.mkdir(parents=True, exist_ok=True)

    entries: list[tuple[str, str, str]] = []

    def add(name: str, source: str, url: str, how: str) -> None:
        entries.append((name, f"Taken from {source}, {how}, downmixed to mono", url))

    for i, (start, end) in enumerate(CUTS, 1):
        length = end - start
        cut = f"atrim={start}:{end},asetpts=PTS-STARTPTS"

        name = f"whisper_hit_{i}.ogg"
        encode(whisper, DREAD / name, f"{cut},{fades(length)}")
        add(name, "DS13-2.0 sound/voice/ghost_whisper.ogg", URL_2, f"cut {start} to {end} s")

        # Slower and lower, with a short room behind it.
        name = f"whisper_low_{i}.ogg"
        encode(whisper, DREAD / name, f"{cut},asetrate=35280,aresample=44100,aecho=0.8:0.5:70:0.25,{fades(length * 1.25)}")
        add(name, "DS13-2.0 sound/voice/ghost_whisper.ogg", URL_2, f"cut {start} to {end} s, slowed to 0.8 and echoed")

        name = f"whisper_back_{i}.ogg"
        encode(whisper, DREAD / name, f"{cut},areverse,atempo=0.92,{fades(length / 0.92)}")
        add(name, "DS13-2.0 sound/voice/ghost_whisper.ogg", URL_2, f"cut {start} to {end} s, reversed and slowed")

    # The bed: three long loops that never line up, and a drone under them.
    encode(whisper, DREAD / "bed_whisper_a.ogg", f"{fades(17.28, 0.3)}")
    add("bed_whisper_a.ogg", "DS13-2.0 sound/voice/ghost_whisper.ogg", URL_2, "whole recording with soft ends")
    encode(whisper, DREAD / "bed_whisper_b.ogg", f"areverse,asetrate=37485,aresample=44100,{fades(20.3, 0.3)}")
    add("bed_whisper_b.ogg", "DS13-2.0 sound/voice/ghost_whisper.ogg", URL_2, "whole recording reversed and slowed to 0.85")
    encode(whisper, DREAD / "bed_whisper_c.ogg", f"asetrate=52038,aresample=44100,highpass=f=600,aecho=0.7:0.4:40:0.3,{fades(14.6, 0.3)}")
    add("bed_whisper_c.ogg", "DS13-2.0 sound/voice/ghost_whisper.ogg", URL_2, "whole recording sped to 1.18, high-passed and echoed")
    encode(drone, DREAD / "bed_drone.ogg", f"atrim=8:56,asetpts=PTS-STARTPTS,{fades(48, 1.5)}", quality=1, level=False)
    add("bed_drone.ogg", "DS13-1.0 sound/ambience/spookyspace1.ogg", URL_1, "cut 8 to 56 s")

    block = [MARK_START]
    for name, copyright_text, url in entries:
        block.append(f'- files: ["{name}"]\n  license: "CC-BY-SA-3.0"\n  copyright: "{copyright_text}"\n  source: "{url}"\n\n')
    block[-1] = block[-1].rstrip("\n") + "\n"
    block.append(MARK_END)

    text = ATTRIBUTIONS.read_text(encoding="utf-8") if ATTRIBUTIONS.exists() else ""
    if MARK_START in text:
        head, rest = text.split(MARK_START, 1)
        tail = rest.split(MARK_END, 1)[1] if MARK_END in rest else ""
        text = head.rstrip("\n") + "\n\n" + "".join(block) + tail
    else:
        text = text.rstrip("\n") + "\n\n" + "".join(block)

    ATTRIBUTIONS.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {len(entries)} files to {DREAD}")


if __name__ == "__main__":
    main()
