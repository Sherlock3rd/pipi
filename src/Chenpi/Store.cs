using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Chenpi;

public sealed class Store
{
    public string DirectoryPath {get;}
    public string PathName=>Path.Combine(DirectoryPath,"pet-state.json");
    public string? Warning {get;private set;}
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    private Task pendingWrite=Task.CompletedTask;
    public string? WriteError {get;private set;}
    public Store(string folder) {DirectoryPath=folder;Directory.CreateDirectory(folder);}
    public PetState Load()
    {
        if(!File.Exists(PathName)&&!File.Exists(PathName+".bak"))return new PetState();
        foreach(string path in new[]{PathName,PathName+".bak"})
        {
            if(!File.Exists(path))continue;
            try
            {
                var s=JsonSerializer.Deserialize<PetState>(File.ReadAllText(path))??throw new Exception("empty state");
                if(s.Version!=1)throw new Exception("不支持的存档版本");
                double[] values={s.Food,s.Water,s.Hunger,s.Thirst,s.Litter,s.Energy,s.Bladder,s.X,s.Y,s.Scale,s.AwakeSeconds,s.LayoutWidth,s.LayoutHeight,s.Volume,s.TotalSeconds,s.RestDuration,s.RestElapsed,s.StillSeconds};
                foreach(var value in values)if(!double.IsFinite(value))throw new Exception("无效存档数值");
                s.FoodClock??=new();s.WaterClock??=new();s.LitterClock??=new();
                foreach(var clock in new[]{s.FoodClock,s.WaterClock,s.LitterClock})
                    if(!double.IsFinite(clock.NextDue)||!double.IsFinite(clock.LastRequested)||clock.UnavailableSince is double since&&!double.IsFinite(since))throw new Exception("无效照料时钟");
                foreach(var p in new[]{s.NestPosition,s.FoodPosition,s.WaterPosition,s.LitterPosition})if(p is Spot at&&(!double.IsFinite(at.X)||!double.IsFinite(at.Y)))throw new Exception("无效物件位置");
                s.Food=Math.Clamp(s.Food,0,100);s.Water=Math.Clamp(s.Water,0,100);s.Hunger=Math.Clamp(s.Hunger,0,100);s.Thirst=Math.Clamp(s.Thirst,0,100);s.Litter=Math.Clamp(s.Litter,0,100);s.Energy=Math.Clamp(s.Energy,0,100);s.Bladder=Math.Clamp(s.Bladder,0,100);s.Scale=Math.Clamp(s.Scale,.7,1.5);
                PetEngine.NormalizeLitterCover(s);
                if(path.EndsWith(".bak"))Warning="主存档异常，已从最近备份恢复。";
                return s;
            }
            catch(Exception e){Log("load",e);}
        }
        throw new InvalidOperationException("存档和备份均无法读取；已保留原文件。请检查 "+DirectoryPath);
    }
    public void Save(PetState s)
    {
        s.SavedAt=DateTimeOffset.UtcNow;
        WriteSnapshot(JsonSerializer.Serialize(s,Options));
    }
    public void QueueSave(PetState s)
    {
        s.SavedAt=DateTimeOffset.UtcNow;
        string snapshot=JsonSerializer.Serialize(s,Options);
        // Snapshot on the UI thread; serialize file writes on a worker, never mutate live state there.
        pendingWrite=pendingWrite.ContinueWith(_=>
        {
            try{WriteSnapshot(snapshot);WriteError=null;}
            catch(Exception e){WriteError=e.Message;try{Log("save",e);}catch{}}
        },TaskScheduler.Default);
    }
    public void Flush()=>pendingWrite.GetAwaiter().GetResult();
    private void WriteSnapshot(string json)
    {
        string temp=PathName+".tmp";
        File.WriteAllText(temp,json);
        if(File.Exists(PathName))File.Replace(temp,PathName,PathName+".bak");else File.Move(temp,PathName);
    }
    public void Log(string label,Exception e)=>File.AppendAllText(Path.Combine(DirectoryPath,"error.log"),$"{DateTimeOffset.Now:o} {label}: {e}\n");
}
