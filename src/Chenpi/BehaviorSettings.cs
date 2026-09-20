using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace Chenpi;

public sealed record BehaviorParameter(string Key,string Group,string Label,double Default,double Min,double Max,string Unit,string Help);

public sealed class BehaviorSettings
{
    public int Version {get;set;}=1;
    public Dictionary<string,double> Values {get;set;}=new();
    public double Get(string key)=>Values.TryGetValue(key,out var value)?value:Catalog.First(p=>p.Key==key).Default;
    public BehaviorSettings Copy()=>new(){Version=Version,Values=new(Values)};
    public static readonly List<BehaviorParameter> Catalog=BuildCatalog();
    private static List<BehaviorParameter> BuildCatalog()
    {
        var p=new List<BehaviorParameter>();
        void Add(string key,string group,string label,double value,double min,double max,string unit,string help)=>p.Add(new(key,group,label,value,min,max,unit,help));
        Add("sleep.delay","rest","安静多久后入睡",30,1,3600,"秒","达到后寻找安全空位，沿已有过渡进入睡姿。");
        Add("rest.min","rest","休息周期下限",20,1,240,"分钟","到期继续休息，不强制起来漫游。");
        Add("rest.max","rest","休息周期上限",60,1,240,"分钟","每轮在上下限间独立抽取。");
        Add("sleep.grace","rest","入睡后照料保护",15,0,300,"秒","暂缓自动照料；点击和拖拽仍可立即响应。");
        foreach(var (kind,name,min,max) in new[]{("food","吃饭",20,60),("water","喝水",10,30),("litter","如厕",60,120)})
        {
            Add(kind+".min",kind,name+"周期下限",min,1,1440,"分钟","实际摄入／如厕后抽取下一期限；不自动扣库存。");
            Add(kind+".max",kind,name+"周期上限",max,1,1440,"分钟","原有已排期任务默认保持，勾选重新计时可重排。");
        }
        Add("request.delay","request","空盆／满砂等待",300,0,7200,"秒","吃喝还必须到达需求期限；仅空盆不请求。");
        Add("request.rotate","request","多项请求轮换间隔",30,1,600,"秒","已有另一项缺货需求时切换。");
        Add("request.sound","request","提醒音最小间隔",35,1,600,"秒","静音时仍只做动作；不会显示猫的台词。");
        Add("guide.radius","guide","跟随距离",190,40,800,"逻辑单位","鼠标跟上才继续带路，离开后完整回头等待。");
        Add("guide.step","guide","每走多远回头",70,10,500,"逻辑单位","只在安全空位停步；不截断转身动画。");
        Add("hover.delay","input","悬停蹭鼠标时间",5,.5,60,"秒","仅醒着且可互动时计时；离开后重新开始。");
        Add("click.window","click","躺姿连续点击窗口",15,1,60,"秒","从第一击起算；第二击不续期，第三击起身。");
        Add("click.awake","input","普通点击连击窗口",6,1,60,"秒","醒着时依次摸摸、伸爪、翻肚皮。");
        Add("drag.hold","input","长按提起时间",.25,.1,2,"秒","按住并移动仍可直接进入拖拽。");
        Add("toy.outer","toy","追羽毛距离",560,120,1200,"逻辑单位","猫中心到羽毛的实际触发距离；判定圈不可见。");
        Add("toy.inner","toy","贴身抓羽毛距离",100,20,400,"逻辑单位","必须小于追逐距离；羽毛离开就继续追。");
        Add("relax.min","gesture","睡姿小动作间隔下限",60,1,3600,"秒","呼吸中等待，完整播放短动作后重新计时。");
        Add("relax.max","gesture","睡姿小动作间隔上限",180,1,3600,"秒","掩面会持续睡眠，单击或照料才放爪。");
        Add("relax.gesture","gesture","选择短动作的概率",100d/3,0,100,"%","剩余概率用于换睡姿；短动作内再按权重选择。");
        Add("wall.enabled","wall","启用屏边扶墙",1,0,1,"开关","只在已靠近实际屏边且无家具阻挡时执行。");
        Add("wall.chance","wall","符合条件时扶墙概率",100,0,100,"%","每次进入休息时判断，未选中就原地睡。");
        Add("wall.duration","wall","扶墙循环停留",5,1,60,"秒","达到停留时间后播完当前循环再落地；本次启动最多自主扶墙一次。");
        Add("walk.callEvery","movement","每几次长行程走路叫",3,0,100,"次","0关闭；剩余路程须容纳完整动作，短行程不插入。");
        Add("sit.callCooldown","movement","坐姿叫声冷却",300,1,3600,"秒","仅坐稳后的短窗口执行，不影响睡姿停留。");
        Add("stand.stretchChance","movement","起身伸展概率",100,0,100,"%","躺姿点击起身后判断；关闭后直接安静待机。");
        Add("night.start","night","夜间开始",23,0,23,"时","使用本地系统时间，可跨午夜；起止相同表示关闭夜间加成。");
        Add("night.end","night","夜间结束",6,0,23,"时","夜间能量不足时进入休息，照料优先级不变。");
        Add("night.grace","night","唤醒后夜间保护",300,0,3600,"秒","避免点击唤醒后立即因夜间规则回睡。");
        foreach(var (pose,name,weight) in new[]{("D","趴卧",1),("A","侧躺",2),("B","露肚",1),("X","舒展",1),("M","掩面",2)})
            Add("pose."+pose,"poses",name+"入睡权重",weight,0,100,"权重","同组归一化为概率；0表示不随机选入，已有过渡仍保留。");
        foreach(var (pose,name) in new[]{("B","露肚"),("X","舒展"),("M","掩面")})
            Add("change."+pose,"changes","侧躺转"+name+"权重",1,0,100,"权重","仅侧躺换姿时比较这三项；其他姿态沿已有路径返回侧躺。");
        foreach(var (pose,name,ids) in new[]{("D","趴卧",new[]{79,89,95}),("A","侧躺",new[]{80,90,96}),("B","露肚",new[]{81,91,97}),("X","舒展",new[]{82,92,98})})
            for(int i=0;i<3;i++)Add("gesture."+ids[i],"gesture-"+pose,name+new[]{"哈欠","叫声","伸懒腰"}[i]+"权重",1,0,100,"权重","对应视频"+ids[i]+"；同姿态内比较，原片速度和时长不变。");
        return p;
    }
    public void Validate()
    {
        if(Version!=1||Values is null)throw new InvalidDataException("不支持的行为配置版本。");
        foreach(var (key,value) in Values)
        {
            var p=Catalog.FirstOrDefault(x=>x.Key==key)??throw new InvalidDataException("未知参数："+key);
            if(!double.IsFinite(value)||value<p.Min||value>p.Max)throw new InvalidDataException($"{p.Label}应在 {p.Min}～{p.Max} {p.Unit}之间。");
            if(p.Unit is "次" or "时" or "开关" &&value!=Math.Truncate(value))throw new InvalidDataException(p.Label+"必须为整数。");
        }
        foreach(string prefix in new[]{"rest","food","water","litter","relax"})if(Get(prefix+".min")>Get(prefix+".max"))throw new InvalidDataException(Catalog.First(p=>p.Key==prefix+".min").Label+"不能大于对应上限。");
        if(Get("toy.inner")>=Get("toy.outer"))throw new InvalidDataException("抓羽毛距离必须小于追逐距离。");
        foreach(string group in new[]{"poses","changes","gesture-D","gesture-A","gesture-B","gesture-X"})
            if(Catalog.Where(p=>p.Group==group).Sum(p=>Get(p.Key))<=0)throw new InvalidDataException("这组权重至少保留一个大于0的选项："+Catalog.First(p=>p.Group==group).Label+"等。");
    }
    public string Choose(Random random,params string[] keys)
    {
        double pick=random.NextDouble()*keys.Sum(Get);
        foreach(string key in keys){pick-=Get(key);if(pick<0)return key;}
        return keys.Last(k=>Get(k)>0);
    }
    public double Range(Random random,string prefix,double unit=1)
    {double lo=Get(prefix+".min")*unit,hi=Get(prefix+".max")*unit;return lo==hi?lo:lo+random.NextDouble()*(hi-lo);}
}

public sealed class BehaviorSettingsStore
{
    public string PathName {get;}
    public string? Warning {get;private set;}
    public BehaviorSettingsStore(string directory)=>PathName=Path.Combine(directory,"behavior-settings.json");
    public static BehaviorSettings Parse(string json){var settings=JsonSerializer.Deserialize<BehaviorSettings>(json)??throw new InvalidDataException("配置为空。");settings.Validate();return settings;}
    public BehaviorSettings Load()
    {
        foreach(string path in new[]{PathName,PathName+".bak"})
        {
            if(!File.Exists(path))continue;
            try{var settings=Parse(File.ReadAllText(path));if(path!=PathName)Warning="行为配置已从备份恢复。";return settings;}
            catch(Exception e) when(e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException){Warning="行为配置读取失败，保留原文件并使用默认值："+e.Message;}
        }
        return new();
    }
    public void Save(BehaviorSettings settings)
    {
        settings.Validate();Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temp=PathName+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));
        if(File.Exists(PathName))File.Replace(temp,PathName,PathName+".bak");else File.Move(temp,PathName);
        Warning=null;
    }
}
