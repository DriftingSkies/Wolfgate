"""Original procedural heat vent: a pressure release that opens with a crack, roars and hisses down to nothing."""
from pathlib import Path
import argparse
import numpy as np
import soundfile

RATE = 44100
DURATION = 4.2
T = np.arange(round(RATE * DURATION)) / RATE


def band(rng, low, high):
    """Noise kept between two soft cutoffs, at unit level."""
    noise = rng.normal(0, 1, len(T))
    f = np.fft.rfftfreq(len(T), 1 / RATE)
    shape = (1 - np.exp(-(f / max(low, 1)) ** 4)) * np.exp(-(f / high) ** 4)
    y = np.fft.irfft(np.fft.rfft(noise) * shape, n=len(T))
    return y / max(np.std(y), 1e-8)


def synth(seed=7):
    rng = np.random.default_rng(seed)
    attack = 1 - np.exp(-T / 0.012)
    # The valve cracks open, then the flow falls away as the hull empties its heat.
    hiss = band(rng, 2500, 9000) * attack * (0.55 * np.exp(-T / 1.5) + 0.25 * np.exp(-T / 0.25))
    roar = band(rng, 300, 1800) * attack * 0.70 * np.exp(-T / 0.9)
    rumble = band(rng, 40, 160) * attack * 0.55 * np.exp(-T / 0.6)
    # The flow flutters as it thins out.
    flutter = 1 + 0.18 * np.sin(2 * np.pi * (11 * T + 3 * T ** 2)) * (1 - np.exp(-T / 0.8))
    crack = band(rng, 800, 6000) * np.exp(-T / 0.018) * 0.9
    y = (hiss + roar + rumble) * flutter + crack
    y *= np.clip((DURATION - T) / 0.5, 0, 1)
    return (y / np.max(np.abs(y)) * 0.85).astype(np.float32)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path, nargs='?', default=Path('Resources/Audio/_WG/ShipShields/vent.ogg'))
    args = parser.parse_args()
    soundfile.write(args.output, synth(), RATE, format='OGG', subtype='VORBIS')
    print(args.output, f'{DURATION} s mono')


if __name__ == '__main__':
    main()
