using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Chenpi;

// Presentation variants never replace the behavior action or its gameplay timing.
public sealed class AnimationVariant
{
    public string Id {get;set;}="";
    public string Group {get;set;}="";
    public string BaseAction {get;set;}="";
    public bool Enabled {get;set;}
    public double Fps {get;set;}=8;
    public bool Loop {get;set;}=true;
}

public sealed class AnimationVariants
{
    private readonly List<AnimationVariant> ready=new();
    private readonly Dictionary<string,string> previous=new();
    private readonly Random random;
    public AnimationVariants(int? seed=null)=>random=seed is int value?new Random(value):new Random();

    public void Load(JsonElement manifest,Func<string,bool> hasFrames)
    {
        ready.Clear();previous.Clear();
        if(!manifest.TryGetProperty("variants",out var list)||list.ValueKind!=JsonValueKind.Array)return;
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var entry in list.EnumerateArray())
        {
            try
            {
                var variant=entry.Deserialize<AnimationVariant>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
                if(variant is null||!variant.Enabled||string.IsNullOrWhiteSpace(variant.Id)||!double.IsFinite(variant.Fps)||variant.Fps<1||variant.Fps>30)continue;
                bool allowed=variant.Group=="click"&&variant.BaseAction is "pet" or "paw" or "roll"
                    ||variant.Group=="idle"&&variant.BaseAction is "idle" or "sit" or "cute";
                if(allowed&&hasFrames(variant.Id)&&ids.Add(variant.Id))ready.Add(variant);
            }
            catch(JsonException){/* Ignore one unfinished entry without losing the other variants. */}
        }
    }

    public AnimationVariant? Choose(string action)
    {
        var candidates=ready.Where(v=>v.BaseAction==action).ToList();
        if(candidates.Count==0)return null;
        if(candidates.Count>1&&previous.TryGetValue(action,out var last))candidates.RemoveAll(v=>v.Id==last);
        var selected=candidates[random.Next(candidates.Count)];previous[action]=selected.Id;return selected;
    }
}
