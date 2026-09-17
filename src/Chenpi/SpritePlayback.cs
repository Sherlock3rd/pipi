using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Chenpi;

public sealed record SpriteClip(int Count,double Fps,bool Loop,double Width=180,double Height=180,double AnchorX=.5,double AnchorY=.921875)
{
    public double Duration=>Count/Fps;
}
public readonly record struct SpriteFrame(string Clip,int Index,SpriteClip Definition);

// Visual transitions never delay gameplay, input, care events or waking the cat.
public sealed class SpritePlayback
{
    private readonly Dictionary<string,SpriteClip> clips=new(StringComparer.Ordinal);
    private readonly Queue<(string Id,bool Reverse)> pending=new();
    private string group="",current="";
    private bool reverse;
    private double started;
    public bool HasFoundation=>clips.ContainsKey("idle")&&clips.ContainsKey("walk")&&clips.ContainsKey("sleep");

    public void Load(JsonElement manifest,Func<string,int> frameCount)
    {
        clips.Clear();Reset();
        if(!manifest.TryGetProperty("clips",out var entries)||entries.ValueKind!=JsonValueKind.Object)return;
        foreach(var item in entries.EnumerateObject())
        {
            try
            {
                var value=item.Value;int count=frameCount(item.Name);
                double fps=value.GetProperty("fps").GetDouble();
                bool loop=value.GetProperty("loop").GetBoolean();
                double width=value.TryGetProperty("width",out var w)?w.GetDouble():180;
                double height=value.TryGetProperty("height",out var h)?h.GetDouble():180;
                double ax=value.TryGetProperty("anchorX",out var x)?x.GetDouble():.5;
                double ay=value.TryGetProperty("anchorY",out var y)?y.GetDouble():.921875;
                if(count>0&&double.IsFinite(fps)&&fps>=1&&fps<=60&&double.IsFinite(width)&&width>0&&width<=512
                    &&double.IsFinite(height)&&height>0&&height<=512&&double.IsFinite(ax)&&ax>=0&&ax<=1&&double.IsFinite(ay)&&ay>=0&&ay<=1)
                    clips[item.Name]=new(count,fps,loop,width,height,ax,ay);
            }
            catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException){ }
        }
    }
    public void Reset(){group="";current="";pending.Clear();started=0;reverse=false;}
    private void Queue(string clip,bool backwards=false){if(clips.ContainsKey(clip))pending.Enqueue((clip,backwards));}
    private void Begin(double now)
    {
        if(pending.Count==0){current="";return;}
        (current,reverse)=pending.Dequeue();started=now;
    }
    public SpriteFrame? Sample(string action,double now)
    {
        if(!double.IsFinite(now)||now<0)return null;
        string next=action switch {
            "idle" or "sit"=>"rest",
            "walk" or "toy-run" or "request-walk" or "guide-walk"=>"move",
            "sleep"=>"sleep", "wake"=>"wake", _=>"other:"+action};
        if(next!=group)
        {
            string before=group;group=next;pending.Clear();
            if(next=="rest")
            {
                if(before=="move")Queue("move-to-sit");
                if(before.Length>0&&before!="sleep")Queue("sit-to-idle");
                Queue("idle");
            }
            else if(next=="move")Queue("walk");
            else if(next=="sleep")
            {
                if(before=="move")Queue("move-to-sit");
                // A saved sleeping pet is already curled when the application opens.
                if(before.Length>0)Queue("sit-to-sleep");
                Queue("sleep");
            }
            else if(next=="wake") {Queue("sit-to-sleep",true);Queue("idle");}
            Begin(now);
        }
        if(current.Length==0)return null;
        var clip=clips[current];
        while(now-started>=clip.Duration&&pending.Count>0)
        {double end=started+clip.Duration;Begin(end);clip=clips[current];}
        double elapsed=Math.Max(0,now-started);
        int index=clip.Loop?(int)(Math.Floor(elapsed*clip.Fps)%clip.Count):(int)Math.Min(clip.Count-1,Math.Floor(elapsed*clip.Fps));
        if(reverse)index=clip.Count-1-index;
        return new(current,index,clip);
    }
}
