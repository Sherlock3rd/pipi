"""Mux WPF-rendered startup frames and the actual emitted mouth cue timeline."""
from pathlib import Path
import json,subprocess,argparse
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('audit',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
route=a.audit/'left';events=json.loads((route/'voices.json').read_text())
timeline=json.loads((route/'timeline.json').read_text());duration=len(timeline)/24
assert len(events)==3 and [e['Frame'] for e in events]==[12,60,109],events
cmd=['ffmpeg','-v','error','-y','-framerate','24','-i',str(route/'%05d.png')]
for e in events:cmd+=['-i',str(ROOT/'assets/audio/cat'/e['File'])]
filters=[]
for i,e in enumerate(events):filters.append(f'[{i+1}:a]adelay={round(e["Time"]*44100)}S[a{i}]')
filters.append('[a0][a1][a2]amix=inputs=3:normalize=0,apad[a]')
cmd+=['-filter_complex',';'.join(filters),'-map','0:v','-map','[a]','-t',str(duration),'-c:v','libx264','-preset','fast','-crf','19','-pix_fmt','yuv420p','-c:a','aac','-b:a','128k','-movflags','+faststart',str(a.output)]
a.output.parent.mkdir(parents=True,exist_ok=True);subprocess.run(cmd,check=True)
print(json.dumps(dict(file=str(a.output),seconds=duration,mouthEvents=events),ensure_ascii=False))
