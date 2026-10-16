"""Publish a local-only review record from source, model and native evidence."""
import hashlib,json,shutil,html
from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1];QA=ROOT/'artifacts/behavior-v6-integration';OUT=ROOT/'art/video-pipeline/behavior-v6'

def main():
    # This report preserves the 140-150 acceptance snapshot. Later grabs have
    # their own report; adding returned files must not inflate this frame count.
    source=[r for r in json.loads((OUT/'returned/sources.json').read_text(encoding='utf-8')) if 140<=r['id']<=150]
    monitor=json.loads((QA/'continuity-final/report.json').read_text(encoding='utf-8'))
    torso=json.loads((QA/'torso-review.json').read_text(encoding='utf-8'))
    scenarios=[];tiles=[];clips={}
    for scenario in ['Head','Back','Back-left','Belly','Paw','Tail','Tail-left','burial-left','burial-right']:
        base=QA/('native-geometry' if scenario.startswith('burial') else 'native-final')/scenario
        trace=json.loads((base/'trace.json').read_text(encoding='utf-8'));samples=trace['Samples']
        hit=trace['Hits'];assert not hit or hit[0]['TransparentRejected']>0
        for n in range(140,151):
            frames={s['Frame'] for s in samples if s['Clip']==f'video-{n}'}
            if not frames:continue
            expected=next(r['frames'] for r in source if r['id']==n);assert frames==set(range(expected)),(scenario,n,len(frames))
            clips[n]=dict(frames=len(frames),first=min(frames),last=max(frames))
            paths=list(base.glob(f'*-video-{n}-*.png'));p=min(paths,key=lambda p:abs(int(p.stem.rsplit('-',1)[1])-48))
            a=Image.open(p).convert('RGBA');bg=Image.new('RGB',a.size,'#ded8ce');bg.paste(a,mask=a.getchannel('A'))
            crop=(100,520,700,790) if not scenario.startswith('burial') else (100,510,1300,790)
            tile=bg.crop(crop);tile.thumbnail((600,270));tiles.append((f'{scenario} / {n}',tile))
        scenarios.append(dict(scenario=scenario,samples=len(samples),hits=hit,finalAction=samples[-1]['Action'],finalCover=samples[-1]['Cover']))
    assert len(clips)==11
    montage=Image.new('RGB',(1200,6*310),'#ded8ce');draw=ImageDraw.Draw(montage)
    for i,(label,tile) in enumerate(tiles):
        x=i%2*600;y=i//2*310;montage.paste(tile,(x,y+30));draw.text((x+10,y+6),label,fill='black')
    montage.save(OUT/'runtime-review.jpg',quality=94)
    tests=(QA/'tests.log').read_text(encoding='utf-8-sig');assert tests.rstrip().endswith('887 checks passed.')
    hashes=all(hashlib.sha256((OUT/f"returned/{r['id']}/source.mp4").read_bytes()).hexdigest()==r['sha256'] for r in source)
    assert hashes
    result=dict(status='local-candidate-art-review-required',sourceHashesVerified=hashes,sourceClips=11,sourceFrames=sum(r['frames'] for r in source),regressionChecks=887,nativeScenarios=scenarios,playedFrames=clips,pending=[151,152],monitor={k:monitor[k] for k in ['frameCount','pairCount','failureCount','reviewCount']},torso={k:torso[k] for k in ['pairs','flagged','lowConfidence','excluded']},artNotes=['149 nose remains about 35 logical pixels above clump; 150 about 14. Horizontal alignment calibrated separately.','145 looks toward camera rather than clearly back toward tail.','Affine monitor flags retained; not a full art pass.'],releaseCreated=False)
    (QA/'summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    docs=ROOT/'docs/qa/behavior-v6';docs.mkdir(parents=True,exist_ok=True)
    shutil.copy2(QA/'summary.json',docs/'summary.json');shutil.copy2(QA/'calibration.json',docs/'calibration.json')
    review=OUT/'review';review.mkdir(exist_ok=True)
    for name in ['report.json','index.html']:
        shutil.copy2(QA/'continuity-final'/name,review/name)
    for p in (QA/'continuity-final').glob('pair-*.png'):shutil.copy2(p,review/p.name)
    for p in QA.glob('torso-pairs-*.jpg'):shutil.copy2(p,review/p.name)
    (OUT/'review.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>陈皮 v6 · 本地返片验证</title><style>body{max-width:1100px;margin:32px auto;padding:0 24px;background:#f1eee8;color:#30382e;font:16px/1.8 system-ui}img{max-width:100%}a{color:#46633a}.note{background:#e5e9db;padding:20px}table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:10px;border-bottom:1px solid #ccc}</style>
<a href="index.html">返回动画预览</a><h1>140—150 · 本地候选已接入</h1><p class="note">11 段、1,115 原帧、24 fps。五部位互动与跳出→回头走近→嗅闻→埋砂已接入。151／152 专用抓棒仍待返片；本轮未提交、未打发行包、未发布。</p>
<h2>已验证</h2><p>887 项共享回归通过；Windows / Mac 宿主本地编译通过。WPF 九个原生渲染场景中，11 段新片的所有原帧号完整播放；五部位及左右背/尾均有实际可见像素命中，透明点被排除。Mac 尚未进行本轮原生桌面验收，以上也不等同于系统鼠标端到端测试。</p>
<h2>保留待审的问题</h2><p>149 右嗅闻低头较浅，鼻尖比污物高约 35 逻辑像素；150 左嗅闻约 14。水平已分别按实际鼻尖校准，未压低或缩放整猫来掩盖高度。145 右向碰尾更像回头看向镜头，是否足够表达回望尾部仍待审。</p>
<p>逐帧尺寸检查共 2,394 帧、2,411 对，保留 405 处仿射阈值警报和 410 处复核提示，包含既有片段及非刚性姿态，<b>不是美术全项通过</b>。固定躯干补充检查 768 对，27 处警报集中在 140/144，590 对低置信度；滚身/跳跃需动态人工评审。下方保留前后及叠图，叠图只用于诊断，运行时仍绘制单帧。</p>
<p><a href="review/index.html">完整原始扫描与高风险帧对</a></p><h2>原生运行场景</h2><img src="runtime-review.jpg" alt="五部位与左右埋砂新动作的原生场景截图"><h2>140 / 144 躯干警报复核</h2>'''+''.join(f'<img src="review/torso-pairs-{i}.jpg" alt="躯干前后帧与叠图 {i+1}">' for i in range(3))+'</html>',encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k not in ['nativeScenarios','playedFrames']},ensure_ascii=False))

if __name__=='__main__':main()
