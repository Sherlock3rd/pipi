"""Install user-approved-as-is returned videos with a common root and native timing."""
from pathlib import Path
import json,shutil,re
from PIL import Image
from extract_basic_video_candidates import extract,CACHE,RUN,OUT,ROOT

assets=ROOT/'assets/pets/bluecat'
manifest=json.loads((assets/'manifest.json').read_text(encoding='utf-8'))
manifest.pop('videoMattePrepared',None);manifest.pop('videoFrameSize',None)
preview=json.loads((OUT/'manifest.json').read_text(encoding='utf-8'))
report=json.loads((RUN/'extraction.json').read_text(encoding='utf-8'))
for clip,files in preview['animations'].items():
    dest=assets/'video-basic-v2'/clip
    shutil.copytree(OUT/clip,dest,dirs_exist_ok=True)
    manifest['animations'][clip]=['video-basic-v2/'+p for p in files]
    manifest['clips'][clip]=preview['clips'][clip]

# Accepted right walk uses the same source interval and duration as before,
# now at native 24 fps, under the common transform used by all returned clips.
d=RUN/'00';d.mkdir(exist_ok=True)
shutil.copy2(ROOT/'art/video-pipeline/walk-v1/incoming/right-walk.mp4',d/'source.mp4')
(d/'source.json').write_text(json.dumps({'frames':121}),encoding='utf-8')
if not (CACHE/'00/stats.json').exists():extract('00')
scale=report['globalScale'];rx,ry=report['sourceRoot'];ox,oy=report['outputRoot']
dest=assets/'video-basic-v2/video-right';dest.mkdir(parents=True,exist_ok=True)
for i,source in enumerate(range(62,101)):
    with Image.open(CACHE/'00'/f'{source:03}.png') as im:
        frame=im.transform((256,256),Image.Transform.AFFINE,
            (1/scale,0,rx-ox/scale,0,1/scale,ry-oy/scale),resample=Image.Resampling.BICUBIC)
        assert frame.getbbox() and min(frame.getbbox()[:2])>0 and max(frame.getbbox()[2:])<256
        frame.save(dest/f'{i:03}.png')
manifest['animations']['video-right']=[f'video-basic-v2/video-right/{i:03}.png' for i in range(39)]
manifest['clips']['video-right']={**preview['clips']['video-14'],'fps':24,'loop':True}
manifest['videoGraph']=True
manifest['artStatus']='user-authorized-as-is-2026-09-18-not-final-art-approval'
(assets/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for c in report['clips']:c['status']='按用户授权原样运行；保留QA记录'
report['runtimeAuthorization']='先不修订，直接修改好放入运行'
(RUN/'extraction.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for c in preview['reviewClips']:c['label']=re.sub(r' · .*$', ' · 已接入运行',c['label'])
preview['artStatus']=manifest['artStatus']
(OUT/'manifest.json').write_text(json.dumps(preview,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
p=ROOT/'src/Chenpi/VideoReviewWindow.cs'
s=p.read_text(encoding='utf-8');s=re.sub(r'\bClip\b','ReviewClip',s)
s=s.replace('查看透明动作与连续衔接。03 歪头、05 摆尾有待修订；原始视频均已保留。','查看全部动作与连续衔接。按现有视频接入运行，原始视频均已保留。')
p.write_text(s,encoding='utf-8')
print('Installed 21 clips + accepted right walk: 2580 native frames; no mirroring.')
