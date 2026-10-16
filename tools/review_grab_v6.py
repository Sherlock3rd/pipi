"""Summarize the four returned grabs and the replacement 154 without publishing."""
from pathlib import Path
import hashlib,json,re,shutil
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[1];QA=ROOT/'artifacts/behavior-v6-integration';OUT=ROOT/'art/video-pipeline/behavior-v6'

def main():
    sources=[r for r in json.loads((OUT/'returned/sources.json').read_text(encoding='utf-8')) if r['id']>=151]
    bounds=json.loads((QA/'grab-source-bounds.json').read_text(encoding='utf-8'))
    monitor=json.loads((QA/'grab-continuity/report.json').read_text(encoding='utf-8'))
    tests=(QA/'grab-tests.log').read_text(encoding='utf-8-sig');checks=int(re.search(r'(\d+) checks passed\.\s*$',tests)[1])
    scenarios=[];tiles=[]
    for scenario,n,idx in [('toy-low-right',151,34),('toy-low-left',152,26),('toy-high-right',153,26),('toy-high-left',154,43)]:
        base=QA/'grab-native-final'/scenario;trace=json.loads((base/'trace.json').read_text(encoding='utf-8'))
        samples=[s for s in trace['Samples'] if s['Clip']==f'video-{n}'];frames={s['Frame'] for s in samples}
        assert frames==set(range(97)),(scenario,frames)
        assert max(s['X'] for s in samples)-min(s['X'] for s in samples)<.001
        scenarios.append(dict(scenario=scenario,frames=len(frames),samples=len(trace['Samples'])))
        paths=list(base.glob(f'*-video-{n}-*.png'));shot=min(paths,key=lambda p:abs(int(p.stem.rsplit('-',1)[1])-idx))
        a=Image.open(shot).convert('RGBA');bg=Image.new('RGB',a.size,'#e4ded4');bg.paste(a,mask=a.getchannel('A'))
        x=samples[0]['X'];tiles.append((scenario,bg.crop((int(x-300),450,int(x+300),790))))
    montage=Image.new('RGB',(1200,740),'#e4ded4');draw=ImageDraw.Draw(montage)
    for i,(label,tile) in enumerate(tiles):
        x=i%2*600;y=i//2*370;montage.paste(tile,(x,y+30));draw.text((x+12,y+8),label,fill='black')
    montage.save(OUT/'grab-runtime-review.jpg',quality=94)
    for r in sources:assert hashlib.sha256((OUT/f"returned/{r['id']}/source.mp4").read_bytes()).hexdigest()==r['sha256']
    result=dict(status='local-candidate-art-review',clips=4,frames=sum(r['frames'] for r in sources),checks=checks,
        native=scenarios,sourceBounds=bounds,replacement154=next(r['sha256'] for r in sources if r['id']==154),
        monitor={k:monitor[k] for k in ['frameCount','pairCount','failureCount','reviewCount']},
        releaseCreated=False,notes=['153 source touches top at frames 25-29; replacement 154 at 43-45 (zero-based).',
        'All raw frames and 24fps retained; 4.04s source duration is longer than the 1.5-2s prompt suggestion.',
        'Full affine monitor includes running, turning and rearing; flags are retained for art review, not an all-clear.',
        'Rear-paw contact tracks supporting feet without scaling the body or changing frame timing.',
        'Native WPF audits are isolated rendering tests, not OS-pointer or macOS native acceptance.'])
    (QA/'grab-summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (ROOT/'docs/qa/behavior-v6/grab-summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    shutil.copytree(QA/'grab-continuity',OUT/'grab-review',dirs_exist_ok=True)
    (OUT/'grab-review.html').write_text(f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>151—154 抓取返片验证</title>
<style>body{{max-width:1100px;margin:40px auto;padding:0 24px;background:#f1eee8;color:#30382e;font:17px/1.8 system-ui}}img{{width:100%;border-radius:12px}}a{{color:#46633a}}.note{{padding:16px;background:#e5e9db}}</style>
<a href="index.html">返回动画预览</a><h1>151—154 · 两组高度抓取</h1>
<p class="note">四段已接入本地候选，154已换成重返版，旧视频按哈希归档。未提交、未推送、未打发行包或更新正式安装。</p>
<p>共388帧，每段97帧、24 fps、4.04秒。低位151/152，羽毛相对支撑面的高度超过85逻辑单位时选择153/154。停步按左右实际爪尖水平偏移校准，抓取中保持朝向和根位置，完整落地后再换高度、真实转向或继续追跑。</p>
<p>{checks}项回归通过；Windows和Mac宿主编译通过。WPF四场景分别完整播放0—96帧；Mac本机原生输入与系统鼠标端到端尚未验证。</p>
<h2>原片与美术待审</h2><p>153第25—29帧、154重返版第43—45帧仍触及原视频上沿（从0计数）；新版154已恢复旧版明显缺失的头顶，但并未达到提示词要求的10%顶部留白。所有源帧保留，不补画耳朵或裁掉问题帧。实际4.04秒长于提示词建议，未擅自加速或剪帧。</p>
<p>尺寸监测扫描{monitor['frameCount']}帧、{monitor['pairCount']}对（包含跑步、转向和接缝），保留{monitor['failureCount']}处阈值警报、{monitor['reviewCount']}处复核提示；含非刚性姿态变化，不能宣称美术全项通过。</p>
<p><a href="grab-review/index.html">查看逐帧前后图、叠图和接缝报告</a> · <a href="review.html">此前140—150验证记录</a></p>
<h2>原生场景截图</h2><img src="grab-runtime-review.jpg" alt="低位左右伸爪与高位左右抓取原生场景"></html>''',encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
