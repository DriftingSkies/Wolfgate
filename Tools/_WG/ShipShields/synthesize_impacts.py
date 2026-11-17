"""Original procedural shield sounds: pitched membrane modes, FM warble, and dark echoes."""
from pathlib import Path
import wave
import argparse
import subprocess
import tempfile
import numpy as np

RATE=48000
DURATION=3.4
OUT=Path.cwd()
T=np.arange(round(RATE*DURATION))/RATE


def coloured_noise(rng, low, high):
    noise=rng.normal(0,1,len(T))
    f=np.fft.rfftfreq(len(T),1/RATE)
    shape=(1-np.exp(-(f/max(low,1))**4))*np.exp(-(f/high)**4)
    y=np.fft.irfft(np.fft.rfft(noise)*shape,n=len(T))
    return y/max(np.std(y),1e-8)


def lowpass(x, cutoff):
    f=np.fft.rfftfreq(len(x),1/RATE)
    return np.fft.irfft(np.fft.rfft(x)*np.exp(-(f/cutoff)**4),n=len(x))


def synth(seed, base, depth, decay, weight):
    rng=np.random.default_rng(seed)
    t=T
    attack=1-np.exp(-t/.004)
    # The pitch settles rapidly into a soft, low membrane resonance.
    pitch=base + base*1.75*np.exp(-t/.035) + base*.36*np.exp(-t/.27)
    lfo_phase=2*np.pi*(4.4*t + 1.0*(1-np.exp(-t/.42)))
    wobble=depth*np.sin(lfo_phase)*(1-np.exp(-t/.018))*np.exp(-t/1.5)
    phase=2*np.pi*np.cumsum(pitch*(1+wobble))/RATE
    body=np.zeros_like(t)
    for ratio,gain,damp in [(1,.60,1),(1.48,.22,.78),(2.13,.13,.50),(3.41,.07,.28),(4.78,.035,.16)]:
        bend=.6*np.sin(phase*.51)*np.exp(-t/.20)
        body+=gain*np.sin(phase*ratio+bend)*attack*np.exp(-t/(decay*damp))
    # A brighter, slightly hollow resonance carries the audible warble above the bass.
    tail_phase=2*np.pi*np.cumsum((base*2.9+160*np.exp(-t/.13))*(1+wobble*1.6))/RATE
    tail=np.sin(tail_phase + .7*np.sin(phase*.49))
    tail*=.13*(1-np.exp(-t/.025))*np.exp(-t/(decay*1.1))*(.78+.22*np.sin(lfo_phase+.6))
    sub_phase=2*np.pi*np.cumsum(43+25*np.exp(-t/.065))/RATE
    sub=.20*weight*np.sin(sub_phase)*(1-np.exp(-t/.008))*np.exp(-t/.24)
    # A tiny filtered contact sound, without a gunshot-like noise burst.
    contact=.075*coloured_noise(rng,160,1800)*attack*np.exp(-t/.027)
    fizz=.018*coloured_noise(rng,650,2300)*(1-np.exp(-t/.018))*np.exp(-t/.13)
    dry=body+tail+sub+contact+fizz
    wet=dry.copy()
    for delay,gain,cutoff in [(.093,.16,1700),(.159,-.11,1450),(.237,.10,1200),(.353,.075,950),(.511,-.05,780),(.719,.035,600)]:
        shift=round(RATE*delay)
        wet[shift:]+=lowpass(dry,cutoff)[:-shift]*gain
    wet-=wet.mean()
    wet=np.tanh(wet*1.05)
    # Loudness-match the audible impact portions; preserve safe playback headroom.
    rms=np.sqrt(np.mean(wet[:RATE*2]**2))
    wet*=min(.115/max(rms,1e-8),.80/max(abs(wet).max(),1e-8))
    wet[:240]*=np.linspace(0,1,240)
    wet[-int(RATE*.25):]*=np.linspace(1,0,int(RATE*.25))**2
    return wet


def save(name, x):
    path=OUT/(name+'.wav')
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes((np.clip(x,-1,1)*32767).astype('<i2').tobytes())
    print(f'{path}: {len(x)/RATE:.2f}s, mono, peak {20*np.log10(max(abs(x).max(),1e-10)):.1f} dBFS, RMS {20*np.log10(np.sqrt(np.mean(x*x))):.1f} dBFS')
    assert np.isfinite(x).all() and abs(x).max()<.9
    assert abs(x[0])<1e-5 and abs(x[-1])<1e-5

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ffmpeg', default='ffmpeg', help='Path to the ffmpeg executable.')
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[3]/'Resources/Audio/_WG/ShipShields')
    args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    global OUT
    with tempfile.TemporaryDirectory(prefix='wolfgate-shields-') as temporary:
        OUT=Path(temporary)
        # Reproduce all three approved previews; the game randomizes the clip and pitch.
        for index,(seed,base,depth,decay,weight) in enumerate([
            (621,104,.065,.43,.7), (622,92,.19,.60,.85), (623,72,.125,.80,1.25)],1):
            name=f'impact_{index}'
            save(name,synth(seed,base,depth,decay,weight))
            destination=args.output/(name+'.ogg')
            subprocess.run([args.ffmpeg,'-hide_banner','-loglevel','error','-y','-i',str(OUT/(name+'.wav')),
                '-map_metadata','-1','-c:a','libvorbis','-q:a','5',str(destination)],check=True)
            decoded=subprocess.run([args.ffmpeg,'-hide_banner','-loglevel','error','-i',str(destination),
                '-f','f32le','-ac','1','-ar',str(RATE),'pipe:1'],check=True,stdout=subprocess.PIPE).stdout
            samples=np.frombuffer(decoded,dtype='<f4')
            assert abs(len(samples)/RATE-DURATION)<.03
            assert np.isfinite(samples).all() and abs(samples).max()<.9
            print(f'{destination}: decoded peak {20*np.log10(abs(samples).max()):.1f} dBFS')


if __name__=='__main__':
    main()