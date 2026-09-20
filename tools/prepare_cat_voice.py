"""Build mouth-aligned PCM copies; preserve the seven supplied MP3 originals."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'assets/audio/cat'
SOURCE = OUT/'source'
SOURCE.mkdir(parents=True, exist_ok=True)
NAMES = {'meow1':'小皮叫声1', 'meow2':'小皮叫声2', 'meow3':'小皮叫声3',
         'meow4':'小皮叫声4', 'lying1':'小皮躺着1', 'lying2':'小皮躺着2', 'annoyed1':'小皮不高兴1'}
# Zero-based visible frames, manually reviewed against the supplied runtime PNGs.
# ClosedFrame is exclusive. Never guess a marker from the nominal action start.
BINDINGS = [(69,43,57,['annoyed1']), (73,31,42,['lying2']), (77,50,59,['meow3']),
            (86,49,61,['meow2']), (87,65,84,['meow3']), (88,35,71,['meow1','meow4']),
            (89,32,67,['lying1']), (90,43,75,['lying1','lying2']), (91,39,72,['lying2'])]
completion_cues=ROOT/'art/video-pipeline/completion-v5/returned/voice-cues.json'
if completion_cues.exists():BINDINGS+=json.loads(completion_cues.read_text(encoding='utf-8'))['Bindings']
rate = 44100
sources = {}
trimmed = {}
for key, name in NAMES.items():
    path = SOURCE/(name+'.MP3')
    if not path.exists():
        shutil.copyfile(Path('C:/Users/liuweichen/Videos')/path.name, path)
    raw = subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-f','f32le','-ar',str(rate),'-ac','1','-'])
    samples = np.frombuffer(raw, dtype='<f4').copy()
    window = 220
    rms = np.sqrt(np.mean(samples[:len(samples)//window*window].reshape(-1,window)**2,axis=1))
    active = np.flatnonzero(rms > max(.003, float(rms.max())*.04))
    start = max(0,int(active[0]*window-rate*.005))
    end = min(len(samples),int((active[-1]+1)*window+rate*.008))
    v = samples[start:end].copy()
    gain = min(8,.30/max(.001,float(np.abs(v).max())))
    v *= gain
    trimmed[key] = v
    sources[key] = dict(File='source/'+path.name,Sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                        OriginalSeconds=len(samples)/rate,TrimStartSeconds=start/rate,
                        TrimEndSeconds=end/rate,Gain=gain)
manifest = dict(Version=2,Sources=sources,Bindings=[],
                Pending=[dict(Clip='video-92',Reason='背向镜头，嘴部被头部遮挡；未确认可见张嘴帧，暂不配音。')])
for clip, opened, closed, choices in BINDINGS:
    sounds=[]
    # Leave a little room for the native output buffer and a frame-boundary stop.
    available=(closed-opened)/24-.02
    for key in choices:
        v=trimmed[key]
        tempo=max(1,len(v)/rate/available)
        factors=[];remaining=tempo
        while remaining>2:factors.append(2);remaining/=2
        factors.append(remaining)
        filters=','.join('atempo='+str(f) for f in factors)
        pcm=subprocess.check_output(['ffmpeg','-v','error','-f','f32le','-ar',str(rate),'-ac','1','-i','-',
             '-af',filters,'-f','f32le','-ar',str(rate),'-ac','1','-'],input=v.astype('<f4').tobytes())
        audio=np.frombuffer(pcm,dtype='<f4').copy()
        # atempo can emit a small rounding tail; limit only within the silence guard.
        audio=audio[:int(available*rate)]
        fade=min(int(.008*rate),len(audio)//2)
        audio[:fade]*=np.linspace(0,1,fade);audio[-fade:]*=np.linspace(1,0,fade)
        file=f'{clip}-{opened}-{key}.wav' if clip>=99 else f'{clip}-{key}.wav'
        with wave.open(str(OUT/file),'wb') as writer:
            writer.setnchannels(1);writer.setsampwidth(2);writer.setframerate(rate)
            writer.writeframes((np.clip(audio,-1,1)*32767).astype('<i2').tobytes())
        sounds.append(dict(File=file,Source=key,Seconds=len(audio)/rate,Tempo=tempo,
                           Sha256=hashlib.sha256((OUT/file).read_bytes()).hexdigest()))
    manifest['Bindings'].append(dict(Clip=f'video-{clip}',OpenFrame=opened,ClosedFrame=closed,Fps=24,Sounds=sounds))
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
print(f"Preserved {len(sources)} originals; prepared {sum(len(b['Sounds']) for b in manifest['Bindings'])} PCM variants for {len(BINDINGS)} mouth windows")
