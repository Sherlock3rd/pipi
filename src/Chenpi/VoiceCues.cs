using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Chenpi;

public sealed record VoiceSound(string File,string Source,double Seconds,double Tempo);
public sealed record VoiceBinding(string Clip,int OpenFrame,int ClosedFrame,double Fps,VoiceSound[] Sounds);
public sealed class VoiceCatalog
{
    public int Version {get;set;}
    public VoiceBinding[] Bindings {get;set;}=Array.Empty<VoiceBinding>();
    public static VoiceCatalog Parse(string json)
    {
        var c=JsonSerializer.Deserialize<VoiceCatalog>(json)??throw new InvalidDataException("叫声配置为空");
        if(c.Version is not (1 or 2)||c.Bindings is null||c.Bindings.Select(b=>(b.Clip,b.OpenFrame)).Distinct().Count()!=c.Bindings.Length)throw new InvalidDataException("叫声配置版本或标记重复");
        foreach(var group in c.Bindings.GroupBy(b=>b.Clip))
        {var windows=group.OrderBy(b=>b.OpenFrame).ToArray();for(int i=1;i<windows.Length;i++)if(windows[i].OpenFrame<windows[i-1].ClosedFrame)throw new InvalidDataException("叫声窗口重叠："+group.Key);}
        foreach(var b in c.Bindings)
        {
            if(b.OpenFrame<0||b.ClosedFrame<=b.OpenFrame||!double.IsFinite(b.Fps)||b.Fps<=0||b.Sounds is null||b.Sounds.Length==0)throw new InvalidDataException("张嘴标记无效："+b.Clip);
            foreach(var s in b.Sounds)
                if(string.IsNullOrWhiteSpace(s.File)||s.File!=Path.GetFileName(s.File)||!s.File.EndsWith(".wav",StringComparison.OrdinalIgnoreCase)||!double.IsFinite(s.Seconds)||s.Seconds<=0||s.Seconds>(b.ClosedFrame-b.OpenFrame)/b.Fps)
                    throw new InvalidDataException("叫声文件或长度无效："+b.Clip);
        }
        return c;
    }
}
public readonly record struct VoiceDecision(bool Stop,VoiceSound? Start);

// Driven only by frames actually handed to the renderer, not by model timers or
// Sample calls used to prepare poses. Repeated paints must never replay a cue.
public sealed class VoiceCues
{
    private readonly Dictionary<string,VoiceBinding[]> bindings;
    private readonly Random random;
    private string previousClip="";
    private int previousFrame=-1;
    private long previousRevision=-1;
    private readonly HashSet<int> fired=new();
    public VoiceCues(VoiceCatalog catalog,int? seed=null)
    {bindings=catalog.Bindings.GroupBy(b=>b.Clip).ToDictionary(g=>g.Key,g=>g.OrderBy(b=>b.OpenFrame).ToArray());random=seed is int n?new Random(n):new Random();}
    public VoiceDecision Observe(string clip,int frame,long revision,bool audible)
    {
        bool fresh=clip!=previousClip||frame<previousFrame||revision!=previousRevision&&frame<=previousFrame;
        if(fresh)fired.Clear();
        previousClip=clip;previousFrame=frame;previousRevision=revision;
        if(!bindings.TryGetValue(clip,out var windows))return new(true,null);
        foreach(var old in windows)if(frame>=old.ClosedFrame||!audible&&frame>=old.OpenFrame)fired.Add(old.OpenFrame);
        if(!audible)return new(true,null);
        var cue=windows.FirstOrDefault(b=>frame>=b.OpenFrame&&frame<b.ClosedFrame);
        if(cue is null)return new(true,null);
        if(fired.Add(cue.OpenFrame))
        {
            // A delayed frame may cross the marker, but never play a stale call
            // after loading, restoring visibility, or seeking deep into a clip.
            if(frame<=cue.OpenFrame+2)return new(true,cue.Sounds[random.Next(cue.Sounds.Length)]);
        }
        return new(fresh,null);
    }
}
