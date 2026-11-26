#!/usr/bin/env python3
"""Original OPSIS sonic identity: deterministic 15 s stereo glass/ambient score.
Usage: python soundtrack.py silent.webm intro.webm [--wav soundtrack.wav]
No video transcoding; no external samples or music.
"""
import argparse, subprocess, tempfile, wave
from pathlib import Path
import numpy as np

SR=48000; DURATION=15; N=SR*DURATION

def smooth(x):
    x=np.clip(x,0,1); return x*x*(3-2*x)

def compose():
    t=np.arange(N)/SR; rng=np.random.default_rng(20261126); mix=np.zeros((N,2))
    def add(signal,start=0,pan=0):
        i=round(start*SR); j=min(N,i+len(signal)); signal=signal[:max(0,j-i)]
        if j>i:
            mix[i:j,0]+=signal*np.sqrt((1-pan)/2); mix[i:j,1]+=signal*np.sqrt((1+pan)/2)
    def glass(f,start,level=.08,length=1.5,pan=0):
        x=np.arange(round(SR*length))/SR
        attack=1-np.exp(-x/.022); tail=smooth((length-x)/.23)
        z=np.zeros_like(x)
        for r,a,d in [(1,1,.65),(2,.22,.33),(3.006,.095,.17),(4.17,.045,.12)]:
            z+=a*np.sin(2*np.pi*f*r*x)*np.exp(-x/d)
        add(level*z*attack*tail,start,pan)
    # Slow wide bed, gradually changing C major colour. No hard beats.
    env=smooth(t/1.7)*(1-smooth((t-13.6)/1.4)); swell=.55+.45*smooth((t-2.5)/4)
    for i,f in enumerate([65.406,130.813,196,261.626,329.628,391.995]):
        mod=.55+.45*np.sin(2*np.pi*(.072+.013*i)*t+i)**2
        for det,pan in [(-.002,-.65),(.002,.65)]:
            sig=np.sin(2*np.pi*f*(1+det)*t+.12*np.sin(2*np.pi*.17*t+i))
            sig+=.16*np.sin(4*np.pi*f*(1+det)*t)
            add(sig*env*swell*mod*(.028 if i<2 else .013),pan=pan)
    # Blue sphere reveal: rising glass shimmer.
    for i,f in enumerate([261.626,391.995,523.251,659.255]):
        glass(f,.55+i*.43,.045,1.35,(-1)**i*.42)
    # Eight app entrances follow the approved visual timing.
    for i,f in enumerate([523.251,587.330,659.255,783.991,659.255,880,783.991,1046.502]):
        glass(f,3.0+i*.22,.027,.75,(-1)**i*.62)
    # A gentle filtered air sweep for convergence.
    x=np.arange(round(2.15*SR))/SR
    white=rng.normal(0,1,len(x)); low=np.convolve(white,np.ones(45)/45,mode='same')
    sweep=np.sin(2*np.pi*(170*x+85*x*x))*.026+low*.06
    add(sweep*smooth(x/.8)*smooth((2.15-x)/.5),8.05,0)
    glass(130.813,9.25,.085,2.1,0); glass(523.251,10.05,.045,1.2,.2)
    # Four-note original signature, timed to final blue logo and slogan.
    for f,start,pan in zip([261.626,329.628,391.995,523.251],[11.65,12.04,12.43,12.87],[-.25,.12,-.12,.25]):
        glass(f,start,.145, min(2.1,15-start),pan)
    # Short stereo diffuse reflection, not an external reverb sample.
    dry=mix.copy()
    for delay,gain in [(0.081,.11),(.143,.075),(.239,.045),(.367,.026)]:
        d=round(delay*SR); mix[d:]+=dry[:-d,::-1]*gain
    mix*= (smooth(t/.08)*smooth((15-t)/.35))[:,None]
    peak=np.max(np.abs(mix)); rms=np.sqrt(np.mean(mix**2))
    mix*=min(.60/max(peak,1e-8),.085/max(rms,1e-8))
    return np.int16(np.clip(mix,-1,1)*32767)

def write_wav(path):
    with wave.open(str(path),'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(compose().tobytes())

def main():
    p=argparse.ArgumentParser(); p.add_argument('video',type=Path); p.add_argument('output',type=Path); p.add_argument('--wav',type=Path); a=p.parse_args()
    if a.video.resolve()==a.output.resolve(): p.error('Use different input and output paths')
    with tempfile.TemporaryDirectory() as tmp:
        wav=a.wav or Path(tmp)/'opsis.wav'; write_wav(wav)
        subprocess.run(['ffmpeg','-v','error','-y','-i',str(a.video),'-i',str(wav),'-map','0:v:0','-map','1:a:0','-c:v','copy','-c:a','libopus','-b:a','128k','-application','audio','-t','15',str(a.output)],check=True)
if __name__=='__main__':main()
