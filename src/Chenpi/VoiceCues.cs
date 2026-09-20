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
        if(c.Version!=1||c.Bindings is null||c.Bindings.Select(b=>b.Clip).Distinct().Count()!=c.Bindings.Length)throw new InvalidDataException("叫声配置版本或片段重复");
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
    private readonly Dictionary<string,VoiceBinding> bindings;
    private readonly Random random;
    private string previousClip="";
    private int previousFrame=-1;
    private long previousRevision=-1;
    private bool fired;
    public VoiceCues(VoiceCatalog catalog,int? seed=null)
    {bindings=catalog.Bindings.ToDictionary(b=>b.Clip);random=seed is int n?new Random(n):new Random();}
    public VoiceDecision Observe(string clip,int frame,long revision,bool audible)
    {
        bool fresh=clip!=previousClip||frame<previousFrame||revision!=previousRevision&&frame<=previousFrame;
        if(fresh)fired=false;
        previousClip=clip;previousFrame=frame;previousRevision=revision;
        if(!bindings.TryGetValue(clip,out var cue))return new(true,null);
        if(!audible){if(frame>=cue.OpenFrame)fired=true;return new(true,null);}
        if(frame>=cue.ClosedFrame){fired=true;return new(true,null);}
        if(!fired&&frame>=cue.OpenFrame)
        {
            fired=true;
            // A delayed frame may cross the marker, but never play a stale call
            // after loading, restoring visibility, or seeking deep into a clip.
            if(frame<=cue.OpenFrame+2)return new(true,cue.Sounds[random.Next(cue.Sounds.Length)]);
        }
        return new(fresh,null);
    }
}
