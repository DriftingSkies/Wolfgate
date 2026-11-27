"""Builds the fortress shield's sounds from the shield generator start-up.

fortress_on.ogg is shield_on.ogg slowed to 0.6 pitch and cut before its tail, fading out over its last 0.8 s.
fortress_hum.ogg is a seamless 4-second loop synthesised from the start-up's tonal peaks (slowed the same way), which
the server fades in under fortress_on.ogg.

    python Tools/_WG/ShipModules/synthesize_fortress.py
"""

from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "Resources/Audio/_WG/ShipShields/shield_on.ogg"
OUT = ROOT / "Resources/Audio/_WG/ShipModules"

RATE = 48000
PITCH_UP, PITCH_DOWN = 5, 3  # 0.6 pitch: 5/3 as many samples at the same rate
CUT = 6.0  # seconds of slowed start-up kept (of 6.9), through its peak
FADE = 0.8  # seconds of fade-out before the cut
LOOP = 4.0  # hum loop length; every partial is a whole number of cycles in it

# Hum partials in Hz (start-up peaks at 79, 108, 116, 201 Hz, times 0.6) and their amplitudes.
PARTIALS = [
    (47.5, 1.0),
    (64.75, 0.7),
    (69.5, 0.55),
    (95.0, 0.5),
    (120.5, 0.35),
    (142.5, 0.25),
    (190.0, 0.18),
    (285.0, 0.08),
]


def write(name: str, data: np.ndarray) -> None:
    sf.write(OUT / name, data.astype(np.float32), RATE, format="OGG", subtype="VORBIS")
    print(f"{name}: {len(data) / RATE:.2f} s")


def start_up() -> np.ndarray:
    data, rate = sf.read(SOURCE)
    assert rate == RATE, rate
    if data.ndim > 1:
        data = data.mean(axis=1)
    slowed = resample_poly(data, PITCH_UP, PITCH_DOWN)[: int(CUT * RATE)]
    fade = int(FADE * RATE)
    slowed[-fade:] *= np.cos(np.linspace(0, np.pi / 2, fade)) ** 2
    return slowed


def hum(rng: np.random.Generator) -> np.ndarray:
    n = int(LOOP * RATE)
    t = np.arange(n) / RATE
    tone = np.zeros(n)
    for frequency, amplitude in PARTIALS:
        tone += amplitude * np.sin(2 * np.pi * frequency * t + rng.uniform(0, 2 * np.pi))

    # Band noise built in the frequency domain over the loop length, so it wraps without a seam.
    spectrum = np.zeros(n // 2 + 1, dtype=complex)
    bins = np.fft.rfftfreq(n, 1 / RATE)
    band = (bins > 180) & (bins < 900)
    spectrum[band] = np.exp(2j * np.pi * rng.uniform(size=band.sum())) / np.sqrt(bins[band])
    noise = np.fft.irfft(spectrum, n)
    noise /= np.abs(noise).max()

    # Slow swell (0.5 Hz and 0.25 Hz, whole cycles in the loop) so the field breathes.
    swell = 1 + 0.12 * np.sin(2 * np.pi * 0.5 * t) + 0.06 * np.sin(2 * np.pi * 0.25 * t + 1.0)
    out = (tone / np.abs(tone).max() + 0.12 * noise) * swell
    return 0.45 * out / np.abs(out).max()


def main() -> None:
    rng = np.random.default_rng(203)
    write("fortress_on.ogg", start_up())
    write("fortress_hum.ogg", hum(rng))


if __name__ == "__main__":
    main()
