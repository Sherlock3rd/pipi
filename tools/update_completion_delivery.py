"""Update the reference pack after returned animation integration."""
from pathlib import Path
import json,csv,io
ROOT=Path(__file__).resolve().parents[1]
def update(out):
    runtime=json.loads((ROOT/'assets/pets/bluecat/manifest.json').read_text(encoding='utf-8'))
    if not runtime.get('completionVideoGraph'):return
    p=out/'manifest.json';m=json.loads(p.read_text(encoding='utf-8'))
    assert all('video-'+str(i) in runtime['animations'] for i in range(99,140))
    m['status']='returned-and-integrated';m['runtimeChanged']=True
    for c in m['clips']:
        c.update(status='returned-and-integrated',runtimeEnabled=True,anchorReviewRequired=False)
        (out/c['folder']/'动作信息.json').write_text(json.dumps(c,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        desc=out/c['file'];text=desc.read_text(encoding='utf-8')
        text=text.replace('状态：待视频制作，未启用。','状态：返片已接入。以下保留原制作要求；检测见 docs/completion-v5-integration.md。')
        desc.write_text(text,encoding='utf-8')
    for a in m['anchors'].values():
        if a['status']=='candidate-awaiting-user-review':a['status']='used-in-returned-video'
    plan=m['startupPlan'];plan.update(status='implemented',enabled=True)
    plan['implementationNote']='117原片两次张嘴，复用首段完整开合嘴一次，24fps共145帧，三声按实际绘制帧12/60/109同步。'
    m['audio']=[dict(id='A01',name='正面三连叫同步音频',status='integrated',note=plan['implementationNote'])]
    p.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (out/'startup-greeting.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    readme=out/'README.md';s=readme.read_text(encoding='utf-8')
    if '> 最新接入状态：' not in s:s='> 最新接入状态：099—139共41段已返片并接入，独立开场已启用；下面待制作描述作为原始要求保留。现状与检测限制见 docs/completion-v5-integration.md。117原片两次张嘴，现复用一次完整开合嘴组成三连叫。\n\n'+s
    readme.write_text(s,encoding='utf-8')
    table=out/'制作清单.csv'
    if table.exists():
        rows=list(csv.reader(io.StringIO(table.read_text(encoding='utf-8-sig'))));column=rows[0].index('状态')
        for row in rows[1:]:row[column]='已返片并接入'
        text=io.StringIO(newline='');csv.writer(text,lineterminator='\n').writerows(rows)
        table.write_text('\ufeff'+text.getvalue(),encoding='utf-8',newline='')
if __name__=='__main__':
    out=ROOT/'art/video-pipeline/completion-v5';update(out)
    from completion_v5_index import build_index,verify_pack
    m=json.loads((out/'manifest.json').read_text(encoding='utf-8'))
    build_index(out,m['clips'],m['anchors']);print(verify_pack(out,m['clips'],m['anchors']))
