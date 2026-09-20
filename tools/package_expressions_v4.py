"""Build v4 external-video delivery; byte-copy shared anchors, never redraw/mirror."""
from pathlib import Path
import csv, hashlib, html, io, json, shutil, zipfile
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'art/video-pipeline/expressions-v4'
BASE=ROOT/'art/video-pipeline/basic-v2'
ANCHORS={'F':'F-正面坐姿.png','SR':'SR-朝右站姿.png','SL':'SL-朝左站姿.png','WR':'WR-右行相位.png','WL':'WL-左行相位.png','D':'D-趴卧.png','A':'A-侧躺收爪.png','B':'B-露肚仰躺.png','C':'C-侧躺舒展.png','M':'M-掩面睡姿.png','HR':'HR-右边扶墙.png','HL':'HL-左边扶墙.png'}
rows=[]
def add(name,start,end,kind,seconds,motion,group):
    n=52+len(rows)
    rows.append(dict(id=n,folder=f'{n:02d}-{name}',name=name,start=start,end=end,type=kind,seconds=seconds,motion=motion,group=group))
for side,s,h in [('右','SR','HR'),('左','SL','HL')]:
    add(f'{side}边抬身扶墙',s,h,'过渡',2.4,f'朝{side}站立，重心移到后腿，两后脚保持地面承重；逐步抬胸伸展身体，将两前掌先后贴到画面{side}侧约定竖直接触面。身体只抬上半身，不能整猫升空或穿过屏幕边缘。', '扶墙')
    add(f'{side}边扶墙微动',h,h,'循环',3.2,'后脚固定支撑，两前爪贴同一竖面，一只前爪向上试探很短距离再收回原接触点，胸背轻伸展、尾尖轻动。无横向位移，不爬出画布，不做人形双脚行走。','扶墙')
    add(f'{side}边落回四足',h,s,'过渡',2.2,'先屈后腿降低胸部，前爪先后离开竖面并落到地面，恢复尾图四足站姿；重量平滑转移，禁止直接倒放抬身动作。','扶墙')
add('坐姿放松趴下','F','D','过渡',2.8,'视线先移向画面右侧远处，前爪小步转向，屈肘降胸、腹部缓缓贴地，进入趴卧；躺下后不看镜头。','平躺衔接')
add('趴卧恢复坐姿','D','F','过渡',2.6,'前掌撑地，先抬胸再收拢后腿，以真实换爪转成尾图正面坐姿；接触脚连续承重，不弹起。','平躺衔接')
add('趴卧放松侧瘫','D','A','过渡',2.4,'胸腹贴地后把重心慢慢移向一侧，放松肩胯，屈前爪、头枕前爪进入侧瘫；眼神持续离开镜头。','平躺衔接')
add('侧瘫翻回趴卧','A','D','过渡',2.3,'先用近侧前爪轻撑地，肩胯顺序滚回胸腹朝下，双前爪向前摆正，头平稳抬至趴卧高度；不看镜头。','平躺衔接')
add('侧瘫翻成露肚','A','B','过渡',2.3,'肩胯一起小幅向后滚，把侧躺重心交给背部，四爪自然屈起成为松弛露肚仰躺；头偏向右侧远处，绝不翻成盯镜头卖萌。','平躺衔接')
add('露肚翻回侧瘫','B','A','过渡',2.2,'轻收后腿，肩胯向同一侧自然滚回，头枕回前爪，恢复侧瘫；不旋转整幅画面。','平躺衔接')
add('侧瘫舒展开四肢','A','C','过渡',2.5,'维持侧躺接触面，慢慢把前后腿舒展开，头降低枕在伸出的前腿附近，进入四肢舒展的低平侧躺。','平躺衔接')
add('舒展收回侧瘫','C','A','过渡',2.4,'放松收回前后爪，身体重心不抬离地面，重新让头枕在弯曲前爪上成为侧瘫。','平躺衔接')
add('趴卧安静呼吸','D','D','循环',4.2,'趴卧不看镜头，胸腹仅有微弱呼吸，耳尖偶尔微动；身体接触面、下巴高度及体积稳定，不准备扑跃。','休息循环')
for a,name in [('A','侧瘫'),('B','露肚'),('C','舒展')]:
    add(name+'平躺呼吸',a,a,'循环',4.8,'保持首图低平休息姿态、视线远离镜头或闭眼，缓慢轻呼吸，尾尖一次很小的松动后归位；不抬头、不自动坐起。循环边界维持自然速度，不在结尾停顿。','休息循环')
    add(name+'首次点击轻抖',a,a,'单次',1.2,'受到一次轻触，肩背皮毛带动躯干轻微颤一下，耳尖微转，四爪仅自然跟随；不抬离地面、不看镜头、不张嘴，随即放松回完全相同的平躺姿态。只有一次轻抖，不能连续抽搐。','点击')
    add(name+'二次点击回头叫',a,a,'单次',2.2,'先做比首次明显但克制的一次肩背抖动，然后保持躯干平躺，仅抬转头向后上方的点击者短暂回望，小幅张嘴叫一次、合嘴；头和爪回到首图位置，重新不看镜头。不能做生气炸毛、露齿咆哮或疼痛痉挛。','点击')
    add(name+'三次点击起身',a,'SR','过渡',3.0 if a=='B' else 2.6,'先把胸腹翻向地面，再将两前掌移到肩下承重，抬胸、收后腿、臀部起立，恢复朝右四足站姿。仰躺须先侧翻，不能像人仰卧起坐；这是独立真实起身，不倒放躺下。','点击')
for a,name in [('D','趴卧'),('A','侧瘫'),('B','露肚平躺'),('C','舒展平躺')]:
    add(name+'打哈欠',a,a,'单次',3.4,'保持原躺姿，先轻吸气、闭眼，头仅小幅抬起，嘴自然由小到大张开一次哈欠，舌尖短暂自然弯起，再缓缓合嘴放回原姿态。嘴部开合约占中间一半时间；不同时伸懒腰、不叫、不看镜头、不改变脸型。','哈欠')
add('侧瘫抬爪掩面入睡','A','M','过渡',2.4,'轻闭双眼，近侧前爪由胸前慢慢抬起，柔软地覆住双眼及鼻梁上部，头仍枕地，另一前爪承托，进入掩面侧睡。','掩面睡眠')
add('掩面睡眠呼吸','M','M','循环',5.6,'前爪始终轻盖双眼，低平侧睡，仅胸腹浅呼吸；爪不滑下露眼，肩胯与头部支撑固定，尾巴不甩动。','掩面睡眠')
add('掩面醒来放爪','M','A','过渡',2.0,'先耳尖微动，覆眼前爪缓慢放回胸前，睁眼至放松半闭状态，头枕回前爪恢复侧瘫。只醒来，不在本段坐起；之后按点击或照料命令接起身。','掩面睡眠')
for a,name in [('WR','向右走路'),('WL','向左走路')]:
    add(name+'叫一声',a,a,'单次',3.25 if a=='WR' else 5.0,'延续首尾相同相位的原地四拍步态，完成整数个完整步态周期。只在中段轻抬下巴、张嘴短叫一次、合嘴，始终看前进方向；不停步、不回头、不变速。根节点不发生整体平移，实际屏幕位移由程序按脚步相位分配。','叫声表现')
for a,name in [('F','坐着'),('D','趴着'),('A','侧瘫'),('B','露肚平躺'),('C','舒展平躺')]:
    add(name+'叫一声',a,a,'单次',1.8,'维持首图身体姿态，仅轻抬下巴，小幅张嘴发出一次自然短叫的口型，随即合嘴并回到首图。躺姿不要起身或看镜头；坐姿允许保持原本视线。不能持续张嘴或反复叫。','叫声表现')
for a,name in [('SR','朝右站立'),('SL','朝左站立')]:
    add(name+'伸懒腰',a,a,'单次',4.0,'前爪向前小步伸出，肩胸向下拉长，臀部自然抬高形成猫式前伸展；短暂停留，胸背收回，四爪依次回到首图站位。尾巴自然平衡，不拱成怒背、不走离原地。','伸懒腰')
for a,name in [('D','趴卧'),('A','侧瘫'),('B','露肚平躺'),('C','舒展平躺')]:
    add(name+'伸懒腰',a,a,'单次',3.6,'保持胸腹/背侧原支撑面，沿身体长轴缓缓伸出前后腿、舒展脊背和脚趾，轻停后松弛收回首图。仰躺只松弛伸爪，不站起来；不加入哈欠、摇头或叫声。','伸懒腰')

# Photo-faithful revision: preserve stable IDs/folders while changing the actual choreography.
REVISED={
52:(3.0,'朝右站立，后脚承重，逐步抬胸至近直立；两前爪先后贴右侧同一竖面，一高一低且屈肘靠身。末段转头回望观察者并略歪头，不做双臂前推。'),
53:(3.2,'后脚固定，两前爪保留一高一低，身体近直立且头保持回望；仅胸腹轻呼吸、耳尖微动及高爪轻试探后归位，不同时伸直双臂。'),
55:(3.0,'朝左站立，后脚承重，逐步抬胸至近直立；两前爪先后贴左侧同一竖面，一高一低且屈肘靠身。末段转头回望观察者并略歪头；独立左侧解剖，不镜像右侧。'),
56:(3.2,'后脚固定，两前爪在左侧保持一高一低，身体近直立且头回望；胸腹轻呼吸、耳尖微动及高爪轻试探后归位，不向墙伸直双臂。'),
60:(4.2,'从朝右趴卧先抬胸轻换爪，在原地真实转向左侧，肩胯顺次侧翻降落；左侧脸贴地，一前爪靠脸弯曲、另一收胸前，右侧后腿一伸一屈。转向由猫自己完成，镜头不转。'),
61:(4.0,'先在侧躺中松开靠脸前爪、收长后腿，翻回胸腹朝下；前掌承重并原地换爪转向右侧，再降胸恢复D。禁止头直接从左换到右。'),
62:(4.4,'从头在左的侧瘫开始，先收腿撑胸，身体在地面低位原地换向，再肩胯同向滚到背部承重，头最终在右侧后仰贴地。两前腿向头顶方向一高一低舒展，嘴闭合；不能只翻肚却瞬间调换头尾。'),
63:(4.2,'露肚先侧翻回胸腹朝下，收前腿并低位换爪转向，再把肩胯慢慢放成头在左的侧瘫，后腿一长一屈、侧脸贴地。完整真实换向，不倒放62。'),
64:(4.2,'侧瘫先轻收四肢、腹部低位承重，以前爪和后腿依次小幅换位转肩转胯，使背部逐渐朝观察者；再侧放躯干、向远处伸前腿，形成近臀远头的背向侧瘫C。镜头固定，不用相机绕拍。'),
65:(4.2,'背向侧瘫先收腿、胸腹低位承重，前后爪逐次换位转肩转胯，让左侧脸自然转回可见位置，再落成A的一伸一屈侧瘫；不可让脸凭空显现。'),
70:(3.8,'先从左向侧瘫收腿、翻胸腹向下，两前掌肩下承重抬胸，收后腿起立，再真实换爪转向朝右站姿SR；身体不弹起，头尾不瞬换。'),
74:(3.4,'从仰躺先侧翻，再把两前腿从头顶收至肩下承重，抬胸、收后腿、起立为朝右站姿；不用人形仰卧起坐。'),
78:(4.0,'背向侧瘫先收腿、翻胸腹朝下，前掌承重抬胸及后躯；真实换爪转向右侧，逐渐显露侧脸，到达SR，不能直接替换正侧面。'),
81:(3.8,'保持B背部承重和头向右后仰，闭眼吸气、缓慢张嘴一次哈欠，舌自然弯起；前腿沿既有一高一低方向轻微伸展，到峰值参考姿势后合嘴松回B。头始终贴近地面，不先坐起。峰值图仅为动作参考，不替代首尾。'),
83:(4.2,'从A侧瘫开始，收回伸长后腿、弯脊柱并将后躯靠向头；肩胯连续卷成紧凑C形团，尾沿外缘收拢。一前爪从胸前抬至额头盖双眼，另一收至口鼻下；最终为M整身蜷团，不只是长侧躺抬手。'),
84:(5.6,'保持紧凑蜷团轮廓，尾沿身体、后腿向头收拢，一前爪盖额眼、另一在口鼻下。仅胸腹浅呼吸；不能在循环边界展开或缩小整猫。'),
85:(4.0,'先耳尖微动，覆眼前爪放下，缓缓展开蜷曲脊背与后躯；一条后腿伸长，另一仍屈，肩胯低位调整到A头在左的侧瘫、脸重新贴地。真实解卷，不把圆团拉伸成直猫。'),
92:(2.2,'保持C背向侧瘫、头朝远处，只轻动下颌短叫一次然后合嘴。背面看不清嘴是正常的，不为展示口型转脸或转镜头。'),
97:(3.8,'沿B背部承重，前腿向头顶方向一高一低继续伸展，脚趾轻张、脊背舒展，后腿自然跟随；闭嘴保持不哈欠，随后松回B。不要把前腿改成胸前抱拳。')}
REVISED.update({
52:(3.0,'朝右站立，后脚承重，抬胸并将身体向右墙倾斜约15—20度；高前爪伸至耳侧、低前爪伸至胸前，两掌先后抵住同一竖面。保持猫式肩肘结构，头回望略倾，尾向左下垂；不是双手握拳靠胸。'),
53:(3.2,'保留向右墙倾斜、上掌耳侧/下掌胸前贴同一竖面，后脚承重，头回望；仅轻呼吸与耳尖微动，前掌不滑离墙面，不改成直身抱拳。'),
55:(3.0,'朝左站立，后脚承重，抬胸并向左墙倾斜约15—20度；高前掌耳侧、低前掌胸前，均抵同一竖面，头回望略倾，尾向右下垂；左右独立解剖，不镜像。'),
56:(3.2,'保持向左墙倾斜、上掌耳侧/下掌胸前贴同一竖面；后脚承重，头回望，轻呼吸及耳尖微动，不收前爪抱拳。'),
83:(4.2,'从A头在左的侧瘫开始收回伸长后腿，弯脊背将后躯包向头，缓慢压低胸颈，左侧头埋入前爪；近侧前臂覆盖眼脸，厚尾沿身体前缘围住，形成低伏侧面椭圆蜷睡。不可翻成俯视露肚圆团或抬头露脸。'),
84:(5.6,'维持低伏侧面椭圆蜷睡，左侧头埋在前爪，近侧前臂遮脸，右侧圆背与后躯收紧、厚尾围身体前缘。只胸腹浅呼吸，不露脸、不翻肚、不在循环接缝伸缩。'),
85:(4.0,'耳尖微动后，遮脸前臂缓慢放回胸前，头轻解埋至左侧脸贴地；尾与后躯逐渐松开，一条后腿伸长另一保留屈曲，展开为A。原地侧视，不翻成正面球、不拉伸整幅图片。')})
for r in rows:
    if r['id'] in REVISED:r['seconds'],r['motion']=REVISED[r['id']]
    if r['id'] in [75,76,77,82,98]:
        r['motion']+=' 保持C背部朝观察者、近臀远头的空间关系；除77指定短暂回头叫以外，脸保持朝远处，不绕相机展示正脸。'

IDENTITY=(BASE/'身份约束.txt').read_text(encoding='utf-8').strip()
PREFIX='固定机位与镜头、1672×941横画幅、浅暖灰干净背景、既有2D绘本厚涂蓝猫。成年蓝灰短毛、圆颊、短深鼻、自然灰嘴垫、轻嘴线和粗尾一致。'+IDENTITY+'只生成一只完整的陈皮；没有墙、家具、人手、屏幕边框、文字、气泡、特效或烘焙阴影。不要变成照片或3D。首尾不单独缩放、裁图、镜像、倒放。头骨和胸廓大小稳定，爪/躯干按支撑面连续承重，尾尖不能决定整猫的高度。'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    review=json.loads((OUT/'修订静态对照.json').read_text(encoding='utf-8'))
    assert review['status']=='revised-candidates-visually-inspected'
    for key in ['D','A','B','C','M','HR','HL']:
        assert sha(OUT/'anchors'/ANCHORS[key])==review['anchors'][key]['sha256'],key
    assert sha(OUT/review['peak']['file'])==review['peak']['sha256'],'peak reference changed since inspection'
    (OUT/'anchors').mkdir(parents=True,exist_ok=True)
    for key in ['F','SR','SL','WR','WL']:shutil.copyfile(BASE/'anchors'/ANCHORS[key],OUT/'anchors'/ANCHORS[key])
    anchor_records={}
    for key,name in ANCHORS.items():
        p=OUT/'anchors'/name
        with Image.open(p) as im:
            assert im.size==(1672,941),(name,im.size)
        anchor_records[key]={'file':'anchors/'+name,'sha256':sha(p),'size':[1672,941],'origin':'basic-v2 byte copy' if key in ['F','SR','SL','WR','WL'] else 'builtin image_gen; photo-based pose candidate'}
    cards=[];table=[]
    for r in rows:
        folder=OUT/r['folder'];folder.mkdir(exist_ok=True)
        for label,key in [('首帧',r['start']),('尾帧',r['end'])]:
            src=OUT/'anchors'/ANCHORS[key];dest=folder/(label+'.png');shutil.copyfile(src,dest);assert sha(src)==sha(dest)
        timing=(f"建议有效动作时长 {r['seconds']:g} 秒。按这个节奏制作，不统一做5秒。平台仅支持固定时长时，选择不短于目标的最近时长，完整动作按目标时长完成，剩余仅保持结束姿态；循环只添加完整周期，不拖慢呼吸或重复倒放。返还未加速、未补帧的原始视频并说明实际时长。")
        seam=('首尾是同一文件，但中间必须有指定动作；循环开始与结束相位和运动速度相容，不在末尾刹停。' if r['type']=='循环' else '动作只发生一次；进入和退出均柔和，结束最多自然稳定约0.2秒。首尾相同时也必须完成中间指定动作，不能输出静帧。')
        if r['start'] in ['WR','WL']: seam='叫声只发生一次；首尾保持原步态相位和非零行走速度，绝不定格或停步。时长优先完整旧步态周期的整数倍；平台时长更长时多走完整周期，不能减速或加速填满。'
        text=f"{r['folder']}\n接缝：{r['start']} → {r['end']}；类型：{r['type']}\n{timing}\n\n{PREFIX}\n\n动作：{r['motion']}\n\n衔接：{seam}严格抵达上传的尾图姿态，不跳切、溶解、变形补间或突然缩放。全程静音生成；口型表现与后期声音分开，不生成语音、台词、音乐。\n"
        if r['id']==81:
            text+='\n额外中间峰值参考：../peaks/B-仰躺哈欠峰值.png。支持额外参考时上传；仅支持首尾时不替换首尾闭嘴B，按本段描述生成中间哈欠。\n'
        if r['id'] in [83,84,85]:
            text+='\n画风一致性：M的r5重绘以同组A为唯一画风图片参考；保持A的柔软毛簇、爪部塑造和胸腹明暗；全程保持同一种毛发笔触密度与灰阶，不得在卷身/解卷时变成浅色斑驳块面、鳞片状或羽毛状粗笔触。姿态与原定时长不变。\n'
        (folder/'提示词.txt').write_text(text,encoding='utf-8')
        (folder/'动作信息.json').write_text(json.dumps(r,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        (folder/'关键词.txt').write_text(f"{r['name']} / {r['seconds']:g}s / {r['start']}→{r['end']} / {r['type']}\n",encoding='utf-8')
        table.append(f"| [{r['folder']}]({r['folder']}/提示词.txt) | {r['start']} → {r['end']} | {r['type']} | {r['seconds']:g}s |")
        pics=''.join(f'<figure><img loading="lazy" src="{r["folder"]}/{label}.png"><figcaption>{label} · {r[key]}</figcaption></figure>' for label,key in [('首帧','start'),('尾帧','end')])
        cards.append(f'<article data-group="{r["group"]}"><h2>{r["folder"]} <small>{r["seconds"]:g}s · {r["type"]}</small></h2><div class="pair">{pics}</div><p>{r["motion"]}</p><p><a href="{r["folder"]}/提示词.txt">完整提示词</a> · <a href="{r["folder"]}/首帧.png">首帧原图</a> · <a href="{r["folder"]}/尾帧.png">尾帧原图</a></p><details><summary>展开可复制提示词</summary><pre>{html.escape(text)}</pre></details></article>')
    manifest={'status':'photo-revised-candidates-awaiting-user-review-and-external-video','revision':'2026-09-20-r5-m-redraw','poseReview':'修订静态对照.json','peakReference':'peaks/B-仰躺哈欠峰值.png','runtimeChanged':False,'generator':'builtin image_gen','anchors':anchor_records,'clips':rows,'clickWindow':{'seconds':15,'starts':'first qualifying click','reset':'fixed window expiry or leave flat-rest','counts':{'1':'same-pose mild twitch','2':'same-pose stronger twitch then one look-back meow','3':'stand to SR'}}}
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    s=io.StringIO();w=csv.writer(s,lineterminator="\n");w.writerow(['编号','动作','首帧','尾帧','类型','目标秒数','分组']);w.writerows([r['id'],r['name'],r['start'],r['end'],r['type'],r['seconds'],r['group']] for r in rows);(OUT/'动作表.csv').write_text(s.getvalue(),encoding='utf-8-sig')
    header=(OUT/'制作说明.md').read_text(encoding='utf-8')
    (OUT/'README.md').write_text(header+'\n\n## 动画动作表\n\n| 动作 / 提示词 | 首尾共用锚点 | 类型 | 目标时长 |\n|---|---|---|---|\n'+'\n'.join(table)+'\n',encoding='utf-8')
    options=''.join(f'<option>{x}</option>' for x in dict.fromkeys(r['group'] for r in rows))
    page='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>陈皮新动作 v4</title><style>body{font:16px/1.7 system-ui;background:#f3f0e8;color:#303632;max-width:1180px;margin:32px auto;padding:20px}article{background:white;padding:20px;margin:22px 0;border-radius:16px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}figure{margin:0}img{width:100%}small{font-size:16px;color:#57665e}pre{white-space:pre-wrap;font:15px/1.8 system-ui}select{padding:8px}a{color:#276b5a}@media(max-width:650px){.pair{grid-template-columns:1fr}}</style><h1>陈皮 · 平躺、扶墙与表情动作 v4</h1>'+f'<p>{len(rows)}条视频任务 · 12张共享锚点 · 逐条独立时长 · 仅制作包，待返片后接入</p><p><a href="README.md">制作说明与动作表</a> · <a href="行为衔接.md">行为树与15秒点击规则</a> · <a href="动作表.csv">CSV动作表</a></p><p>上传每条的首帧和尾帧原文件，复制完整提示词。循环首尾相同是有意设计。所有时长是制作目标，原素材不批量变速。</p><label>动作分类 <select id="filter"><option value="">全部</option>{options}</select></label>'+''.join(cards)+'<script>document.getElementById("filter").onchange=e=>document.querySelectorAll("article").forEach(x=>x.hidden=!!e.target.value&&x.dataset.group!==e.target.value)</script></html>'
    gallery='<article><h2>2026-09-20 照片姿态修订版</h2><p>7张关键图已重画，另附哈欠峰值；等待用户美术评审与返片动态验收。<a href="静态审核.md">逐图对照与限制</a></p><div class="pair">'+''.join(f'<figure><img src="anchors/{ANCHORS[k]}"><figcaption>{k} · {html.escape(review["anchors"][k]["observation"])}</figcaption></figure>' for k in ['HR','HL','A','B','C','M','D'])+'<figure><img src="peaks/B-仰躺哈欠峰值.png"><figcaption>哈欠中间峰值；不能代替循环首尾</figcaption></figure></div></article>'
    page=page.replace('<label>动作分类',gallery+'<label>动作分类')
    (OUT/'index.html').write_text(page,encoding='utf-8')
    # Verify every new resting state is reachable from F and can get back to F
    # through generated transitions or the existing SR->F (07), SL->F (09).
    edges=[(r['start'],r['end']) for r in rows]+[('SR','F'),('SL','F'),('F','SR'),('F','SL')]
    def reach(a,b):
        seen={a}
        while True:
            nxt=seen|{v for u,v in edges if u in seen}
            if nxt==seen:return b in seen
            seen=nxt
    for a in ['D','A','B','C','M','HR','HL']:assert reach('F',a) and reach(a,'F'),a
    for a in ['A','B','C']:
        assert all(any(r['start']==a and token in r['name'] for r in rows) for token in ['首次点击','二次点击','三次点击','打哈欠','叫一声','伸懒腰'])
    result={'anchors':len(ANCHORS),'clips':len(rows),'endpointCopies':len(rows)*2,'byteIdenticalSharedAnchors':True,'allNewStatesHaveEntryAndExit':True,'threeFlatPosesHaveAllInteractions':True,'secondsRange':[min(r['seconds'] for r in rows),max(r['seconds'] for r in rows)],'dynamicQA':'pending returned video; static input validation only'}
    (OUT/'校验结果.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False))
if __name__=='__main__':main()
