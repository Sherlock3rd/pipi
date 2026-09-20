"""Package the agreed missing-animation backlog and dormant startup choreography.
No images are generated, changed or installed by this script.
"""
import csv
import hashlib
import io
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'art/video-pipeline/completion-v5'
OUT.mkdir(parents=True, exist_ok=True)
items = []


def add(name, start, end, priority, group, seconds, motion, reason):
    items.append(dict(id=99+len(items), name=name, start=start, end=end, priority=priority,
                      group=group, suggestedSeconds=seconds, motion=motion, reason=reason,
                      status='awaiting-production', runtimeEnabled=False))


for side, stand in [('右','SR'),('左','SL')]:
    add('侧坐直接朝'+side+'站起','I',stand,'P1','基础衔接','2—3','前掌承重，后腿从侧坐收拢并推起，直接恢复四足站立，不先转正面坐姿。','蜷睡醒来21→I后可直接起身，消除18→06/08绕路。')
for stand, side in [('SR','右'),('SL','左')]:
    add('朝'+side+'站姿直接趴卧',stand,'D','P1','基础衔接','2—3','四足站立逐渐屈肘降胸，胸腹接地并收拢四爪进入D，不经过正面坐下。','减少吃喝后到空位又坐下再趴卧。')
for stand, side in [('SR','右'),('SL','左')]:
    add('趴卧直接朝'+side+'站起','D',stand,'P1','基础衔接','2—3','前掌移到肩下支撑，胸腹抬起，再伸直后腿成为四足站姿，不先坐起。','趴卧醒来可直接移动。')
for pose, name in [('A','侧躺'),('B','露肚'),('X','舒展')]:
    add(name+'直接朝左站起',pose,'SL','P2','左向起身','2—3','先回胸腹朝下，四爪真实承重后直接朝左站起；不能左右镜像或倒放已有右起身。','对应已有70/74/78右起身，减少起身后15转左。')
for side, stand, run in [('右','SR','RR'),('左','SL','RL')]:
    add('朝'+side+'跑步起步',stand,run,'P0','开场跑步','0.6—1.0','后腿蹬地，前腿前探，身体自然前倾，进入真实猫小跑步态；镜头固定，根节点位移由程序控制。','开场快速来到中间；不加速旧走路代替。')
    add('朝'+side+'跑步循环',run,run,'P0','开场跑步','0.8—1.2','稳定轻快小跑，四肢接地顺序正确，首尾为同一落脚相位，躯干轻微自然起伏；尾巴平衡。','真实跑步循环需测量脚底位移与世界速度，不能滑步。')
    add('朝'+side+'跑步停稳',run,stand,'P0','开场跑步','0.8—1.2','从共用跑步相位逐步减速，前后脚真实落地承重，收稳为四足站姿；不急停冻结或突然坐下。','跑到中点后衔接踱步。')
for stand, side in [('SR','右'),('SL','左')]:
    add('朝'+side+'站姿转正面四足',stand,'SF','P0','开场互动','1.0—1.8','四足保持站立，依次换爪小步转向镜头，身体透视连续，最后正面四足站稳；不坐下。','蹭头前必须真正面向前方。')
add('正面站立向前蹭头','SF','SF','P0','开场互动','2—3','四脚连续承重，头颈温柔前探，额头向前轻蹭两下，轻闭眼后收回SF；不生成手或物品，不放大整猫冒充贴近。','38是坐姿蹭手，不能替代先站着蹭头、最后才坐下的开场顺序。')
add('正面站立连续叫三次','SF','SF','P0','开场互动','3—4','仅一段连续动作：张嘴叫一次、合嘴短停，再叫第二次、合嘴，再叫第三次、合嘴收回SF；严格三次，不哈欠。','88从坐姿开始，不用于本开场。返片需标出三个张嘴峰值供声音同步。')
add('正面四足平稳坐下','SF','F','P0','开场互动','1.2—2','后腿自然收拢，臀部降低，前掌稳定承重，尾巴顺势收好，落到已确认F坐姿，结束时不再次起身。','开场最终坐定，随后交回正常行为树。')
for side, pose in [('右','SR'),('左','SL')]:
    add('朝'+side+'站立致谢',pose,pose,'P2','照料衔接','1.5—2.5','保持四足站姿，轻眯眼并微微点头或轻摆尾一次，再回原站姿；不坐下、不跨姿态硬切。','可替代站姿示意后07→51的坐姿致谢，减少不必要坐下。')
for start, end, name in [('C','D','蜷睡转趴卧'),('D','C','趴卧卷成蜷睡'),('C','A','蜷睡放松侧躺'),('A','C','侧躺卷成蜷睡')]:
    add(name,start,end,'P3','低位换姿','2—3','在地面接触面上缓慢展开或收拢身体，肩胯顺序变换、头爪自然归位，全程不站起；首尾严格匹配姿态图。','可选低位换姿，减少旧睡姿切新睡姿时起身。')
for pose, name in [('D','趴卧'),('A','侧躺'),('B','露肚'),('X','舒展'),('M','掩面')]:
    hold='H_'+pose
    add(name+'被提起',pose,hold,'P3','拖拽补齐','0.8—1.5','由当前真实躺姿被轻轻托提，身体重力下垂、四肢自然收拢，进入专属悬空姿态；不硬切正面坐姿，不生成手。','当前拖拽保留原姿态；本段用于补齐真实起提过渡。')
    add(name+'对应悬空轻摆',hold,hold,'P3','拖拽补齐','2—3','保持对应悬空姿态，轻微重力摆动，四肢和尾巴自然活动后回同相位；无激烈挣扎。','悬空循环需独立匹配本姿态起提。')
    add(name+'对应放回地面',hold,pose,'P3','拖拽补齐','1—2','下降时脚掌或胸腹先接触地面，身体重量自然落稳，回到原地面姿态；不是把起提倒放。','回到原躺姿的独立落地片。')
assert len(items)==41 and items[-1]['id']==139

anchors={}
basic=ROOT/'art/video-pipeline/basic-v2/anchors'
expressions=ROOT/'art/video-pipeline/expressions-v4/anchors'
for pose, file in {'F':'F-正面坐姿.png','SR':'SR-朝右站姿.png','SL':'SL-朝左站姿.png','I':'I-侧坐.png','C':'C-蜷睡.png'}.items():
    anchors[pose]={'source':str((basic/file).relative_to(ROOT)).replace('\\','/')}
for pose,file in {'D':'D-趴卧.png','A':'A-侧躺收爪.png','B':'B-露肚仰躺.png','X':'C-侧躺舒展.png','M':'M-掩面睡姿.png'}.items():
    anchors[pose]={'source':str((expressions/file).relative_to(ROOT)).replace('\\','/')}
for pose,entry in anchors.items():
    entry.update(status='existing-reference',sha256=hashlib.sha256((ROOT/entry['source']).read_bytes()).hexdigest(),archiveFile='references/'+pose+'.png')
for pose in ['RR','RL','SF','H_D','H_A','H_B','H_X','H_M']:
    anchors[pose]={'status':'new-anchor-required','source':None,'note':'待新建并审阅，不以旧姿态改名顶替。'}

plan={
 'id':'startup-greeting','status':'awaiting-independent-running-and-front-standing-assets','enabled':False,
 'userDecision':'等独立跑步素材补齐后再启用开场；不加速现有走路替代。',
 'triggers':['first-valid-run','os-autostart-once-per-boot'],
 'excludedTriggers':['ordinary-relaunch','host-recreation','wake-from-sleep','settings-open'],
 'requiredNewClips':list(range(108,119)), 'preferredTransitions':[99,100],
 'stages':[
  {'id':'sleep','name':'窝内睡眠','clips':[20],'next':'wake'},
  {'id':'wake','name':'醒来离窝','preferredClips':[21,99,100],'existingFallback':[21,18,6,8],'next':'run'},
  {'id':'run','name':'快速跑到中部','clips':[108,109,110,111,112,113],'next':'pace'},
  {'id':'pace','name':'中部来回踱步','existingClips':['10','right','11','15','12','14','13','16'],'next':'rub'},
  {'id':'rub','name':'面向前方蹭头','clips':[114,115,116],'next':'call'},
  {'id':'call','name':'站着连叫三次','clips':[117],'count':3,'next':'sit'},
  {'id':'sit','name':'中部平稳坐定','clips':[118],'next':'normal'},
  {'id':'normal','name':'交回正常行为树','clips':[1],'next':None}],
 'proposedTuning':{'sleepLeadSeconds':1.5,'paceRoute':'中点→左侧空位→右侧空位→中点','greetingCount':3,'note':'时间和踱步范围为制作建议，尚未成为运行参数。'},
 'constraints':['中点被家具占用时找最近可坐空位，不搬物品','世界位移与脚底速度同步，左右不用镜像','离窝持续计算坐垫支撑与低窝沿遮挡','照料期限继续计时，表演不扣库存、不重抽期限','拖拽/手动命令可取消，取消后不自动重播','全屏/主动隐藏期间暂停演出进度，恢复后接续','三次叫声动作始终保留，声音遵从静音设置'],
 'activationGate':['全部必需返片及新RR/RL/SF锚点通过审阅','透明提取、跑步根位移和姿态图接入完成','片内相邻帧/循环/接缝/承重脚/窝沿场景审计通过','首启、自启去重、输入取消、全屏暂停和普通行为恢复测试通过']}

identity='全程2D蓝猫，保持现有B绘本厚涂、轻嘴线、自然灰色嘴垫和身份。自身左眼略小，正面时为画面右眼，不镜像调换；固定机位和比例，不变焦、不改毛色、不生成背景物品、台词、文字或气泡。脚底/胸腹支撑连续，四肢数量正确，不逐帧归一化外框。'
for item in items:
    folder=OUT/f"{item['id']:03}-{item['name']}";folder.mkdir(exist_ok=True)
    item['file']=str((folder/'制作说明.md').relative_to(OUT)).replace('\\','/')
    def ref(p):
        return '已有参考：'+anchors[p]['source'] if anchors[p]['source'] else '缺少新锚点：'+p+'，先画标准姿态并审阅，不能直接投视频。'
    text=f"# {item['id']} · {item['name']}\n\n状态：待制作，未启用。优先级：{item['priority']}。分组：{item['group']}。\n\n- 首姿态：{item['start']}。{ref(item['start'])}\n- 尾姿态：{item['end']}。{ref(item['end'])}\n- 建议时长：{item['suggestedSeconds']}秒；按真实动作完成时长返片，不凑固定帧数。\n- 用途：{item['reason']}\n\n## 动作提示词\n\n{identity}\n\n{item['motion']}\n\n严格从共享首锚点开始，收回共享尾锚点。循环需首尾相位和速度连续；过渡末端完整接住下一姿态。\n\n## 返片验收\n\n源视频保留原字节及帧率；核对所有相邻帧、循环和行为接缝，宽高分开测量，承重脚/胸腹支撑单独检查。大透视形变不得当作已通过。跑步增加脚底位移与真实速度标定；开场增加左右离窝与家具避让场景检查。\n"
    (folder/'制作说明.md').write_text(text,encoding='utf-8',newline='\n')
manifest={'version':1,'status':'production-backlog-not-runtime-assets','runtimeChanged':False,'clips':items,'anchors':anchors,'startupPlan':plan,
          'audio':[{'id':'A01','name':'正面三连叫同步音频','status':'missing','note':'需合法来源的自然猫叫，按117的三个张嘴峰值同步；当前合成提示音不是最终猫叫素材。'}],
          'refinementNotMissing':[{'clips':[86],'issue':'与旧右行的步态相位统一'},{'clips':[62,63,74],'issue':'翻身/起身透视与躯干形体精修'}]}
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
(OUT/'startup-greeting.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
buffer=io.StringIO(newline='');writer=csv.writer(buffer,lineterminator='\n');writer.writerow(['编号','动作','优先级','分组','首姿态','尾姿态','建议秒数','状态','用途'])
for c in items:writer.writerow([c['id'],c['name'],c['priority'],c['group'],c['start'],c['end'],c['suggestedSeconds'],'待制作，未启用',c['reason']])
(OUT/'制作清单.csv').write_text('\ufeff'+buffer.getvalue(),encoding='utf-8',newline='')
rows='\n'.join(f"| {c['id']} | [{c['name']}]({c['file']}) | {c['start']}→{c['end']} | {c['priority']} | {c['group']} |" for c in items)
readme=f'''# 陈皮待补动画合集 v5

共 **41条待制作动画（99—139）**、**8个待建立姿态锚点**、**1项三连叫同步音频**。不是41条都必须先做：P0的11条用于新开场，P1的6条优先消除日常绕路；其他按需补齐。当前没有制作新图片或视频，也没有启用开场。

用户明确：**等独立跑步素材补齐后再启用开场**。现有走路不能加速冒充跑步。开场另需正面四足站姿及蹭头/三连叫/坐下素材，不能先坐下再拿坐姿动画替代。

## 制作顺序

1. 先准备RR右跑相位、RL左跑相位、SF正面四足站姿三个新标准图；逐图审阅身份、眼睛、脚底和体型。
2. 制作108—118共11条开场必需片，优先一起补99/100侧坐直接起身。
3. 补101—104站立与趴卧直连、105—107左向起身、119/120站姿致谢。
4. 121—139为可选低位换姿及五种躺姿的起提/悬空/落地；H_D/H_A/H_B/H_X/H_M五个悬空锚点在这阶段再做。

## 独立启动开场

```mermaid
flowchart TD
 T[首次有效运行 / 开机自启一次] --> G{{必需素材、接入与检测全部通过？}}
 G -->|否：当前状态| N[正常行为树，不播放开场]
 G -->|通过后启用| S[猫窝内蜷睡]
 S --> W[醒来并离窝]
 W --> R[真实跑步到屏幕中部空位]
 R --> P[左右来回踱步，再回中点]
 P --> F[转为正面四足站姿]
 F --> H[向前蹭蹭脑袋]
 H --> C[单段连续叫三次]
 C --> D[在中部平稳坐下]
 D --> N
```

开场对外表现为一段连续的专属演出，内部共用姿态端点，以适应猫窝位置和屏幕宽度。**当前是流程设计，尚未接入自动触发。** 普通重开、宿主重建和休眠恢复不重复播放；首次运行与自启同时满足只播一次。触发去重应在开始时持久化，取消或中途退出也不自动重来。

中部被家具占用时选最近可坐空位；不搬物品、不坐在盆里。照料期限继续计时、库存不变，完整坐定才交回普通调度。手动拖拽/命令可取消开场。全屏/主动隐藏应暂停表演进度，声音遵守静音设置。待接入时开放窝内等待、踱步范围/往返、跑步物理速度等参数；目前这些不是已生效的运行参数。

镜头固定、不把猫窝烘焙进角色视频；猫窝是场景物件，离窝支撑与前沿遮挡由程序计算。三次叫声只在117内发生，需给出三个开嘴峰值时间点；三次之后合嘴，118才坐下。

## 完整清单

| 编号 | 动作 | 首尾 | 优先级 | 分组 |
|---|---|---|---|---|
{rows}

## 现有素材与缺口边界

- 已有：左右走/转身、20蜷睡、21醒来、38坐姿蹭手、88坐姿叫、70/74/78右起身，均不重复列为缺片。
- 119/120站姿致谢可减少补给后为了51坐姿致谢而坐下；当前49→07→51是有效现有路径。
- 86右走叫步态相位、62/63翻身和74起身形体属于返片精修，不是没有动作。
- C表示旧蜷睡；X表示新舒展（v4原图文件名C-侧躺舒展），不能把两个C混为同姿态。
- H_*为待设计的对应悬空姿态；没有借用旧H改名冒充匹配。
- 当前静音规则不变；自然猫叫音频待补，合成提示音不是最终音效。

manifest.json记录每个已有参考的项目路径及SHA256；制作压缩包的references目录包含这10张原字节参考图。每条文件夹内都有动作要求、首尾姿态、建议时长、用途和检测要求。
'''
(OUT/'README.md').write_text(readme,encoding='utf-8',newline='\n')
archive=ROOT/'artifacts/completion-v5/陈皮-待补动画合集-v5.zip';archive.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
    for file in sorted(OUT.rglob('*')):
        if file.is_file():z.write(file,str(file.relative_to(OUT)))
    for pose, entry in anchors.items():
        if entry['source']:z.write(ROOT/entry['source'],entry['archiveFile'])
with zipfile.ZipFile(archive) as z:assert z.testzip() is None
assert [c['id'] for c in items if c['priority']=='P0']==list(range(108,119))
assert all(p in anchors for c in items for p in [c['start'],c['end']])
print(f'{len(items)} missing clips, {sum(a["source"] is None for a in anchors.values())} new anchors; archive CRC passed: {archive}')
