"""Audition actual authored frames with the same reviewed mouth/audio markers."""
from pathlib import Path
import json
import subprocess
from PIL import Image, ImageDraw

root=Path(__file__).resolve().parents[1]
out=root/'artifacts/cat-voice/review';out.mkdir(parents=True,exist_ok=True)
pet=root/'assets/pets/bluecat'
animation=json.loads((pet/'manifest.json').read_text(encoding='utf-8'))
voices=root/'assets/audio/cat'
catalog=json.loads((voices/'manifest.json').read_text(encoding='utf-8'))
names=[]
for binding in catalog['Bindings']:
    clip=binding['Clip'];paths=animation['animations'][clip];fps=binding['Fps']
    with Image.open(pet/paths[0]) as im: width,height=im.size
    label=Image.new('RGBA',(width,38),(242,240,232,255))
    ImageDraw.Draw(label).text((10,12),f"{clip} | mouth opens frame {binding['OpenFrame']} | {binding['Sounds'][0]['Source']}",fill='black')
    label.save(out/(clip+'-label.png'))
    delay=round(binding['OpenFrame']/fps*44100)
    target=out/(clip+'.mp4')
    subprocess.run(['ffmpeg','-v','error','-y','-framerate',str(fps),'-i',str((pet/paths[0]).parent/'%03d.png'),
        '-i',str(voices/binding['Sounds'][0]['File']),'-f','lavfi','-i',f'color=c=0xeeeae4:s={width}x{height}:r={fps}',
        '-i',str(out/(clip+'-label.png')),'-filter_complex',
        f'[2:v][0:v]overlay=shortest=1[cat];[cat][3:v]overlay=0:0,format=yuv420p[v];[1:a]adelay={delay}S,apad[a]',
        '-map','[v]','-map','[a]','-t',str(len(paths)/fps),'-c:v','libx264','-preset','fast','-crf','20','-c:a','aac','-b:a','128k','-movflags','+faststart',str(target)],check=True)
    names.append(target.name)
(out/'concat.txt').write_text(''.join(f"file '{name}'\n" for name in names),encoding='utf-8')
subprocess.run(['ffmpeg','-v','error','-y','-f','concat','-safe','0','-i',str(out/'concat.txt'),'-c','copy','-movflags','+faststart',str(out/'cat-voice-sync.mp4')],check=True)
print(out/'cat-voice-sync.mp4')
