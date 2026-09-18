using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Chenpi;

public sealed record SpriteClip(int Count,double Fps,bool Loop,double Width=180,double Height=180,double AnchorX=.5,double AnchorY=.921875,bool MirrorWithFacing=true)
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
    private bool videoGraph;
    private bool careGraph;
    private string careAction="",careExit="";
    private double careStarted,exitStarted;
    private string pose="F",destination="F";
    private static readonly (string From,string To,string Clip)[] edges={
        ("F","SR","06"),("SR","F","07"),("F","SL","08"),("SL","F","09"),
        ("SR","WR","10"),("WR","SR","11"),("SL","WL","12"),("WL","SL","13"),
        ("SR","SL","15"),("SL","SR","16"),("F","I","17"),("I","F","18"),
        ("I","C","19"),("C","I","21")};
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
                    clips[item.Name]=new(count,fps,loop,width,height,ax,ay,
                        !value.TryGetProperty("mirrorWithFacing",out var mirror)||mirror.GetBoolean());
            }
            catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException){ }
        }
        videoGraph=manifest.TryGetProperty("videoGraph",out var graph)&&graph.GetBoolean()
            &&clips.ContainsKey("video-01")&&clips.ContainsKey("video-14")&&clips.ContainsKey("video-right");
        careGraph=manifest.TryGetProperty("careVideoGraph",out var care)&&care.GetBoolean()&&clips.ContainsKey("video-22");
    }
    public void Reset(){group="";current="";careAction=careExit="";pending.Clear();started=0;reverse=false;pose=destination="F";}
    private static string[] CareSequence(string action,bool left)=>action switch {
        "eat"=>new[]{"22","23","24"},"drink"=>new[]{"22","25","24"},
        "toilet"=>new[]{"26","27","28"},"bury"=>new[]{"29","30","31"},
        "drag"=>new[]{"32","33"},"land"=>new[]{"34"},
        "toy-bat"=>left?new[]{"36"}:new[]{"29","35"},
        "pet"=>new[]{"37"},"rub"=>new[]{"38"},"paw"=>new[]{"39"},"roll"=>new[]{"40","41","42"},
        "guide-look"=>new[]{left?"44":"43"},
        "request-food"=>new[]{"45"},"request-water"=>new[]{"46"},"request-litter"=>new[]{"47"},
        "guide-food"=>new[]{"48"},"guide-water"=>new[]{"49"},"guide-litter"=>new[]{"50"},"care-thanks"=>new[]{"51"},_=>Array.Empty<string>()};
    public double? ActionDuration(string action,bool left=false)
    {
        if(!careGraph)return null;var seq=CareSequence(action,left);if(seq.Length==0)return null;
        double sum=0;foreach(var id in seq){if(!clips.TryGetValue("video-"+id,out var clip))return null;sum+=clip.Duration;}return sum;
    }
    public (double Start,double Duration)? ConsumptionWindow(string action)
    {
        if(!careGraph||action is not ("eat" or "drink"))return null;
        return(clips["video-22"].Duration,clips[action=="eat"?"video-23":"video-25"].Duration);
    }
    private SpriteFrame? SampleCare(string action,double now,bool left)
    {
        var sequence=CareSequence(action,left);
        if(sequence.Length>0)
        {
            string key=action+(action is "toy-bat" or "guide-look"?(left?"-L":"-R"):"");
            if(key!=careAction){careAction=key;careStarted=now;careExit="";current="";group="care";pending.Clear();}
            double elapsed=now-careStarted;
            for(int i=0;i<sequence.Length;i++)
            {
                string id="video-"+sequence[i];var clip=clips[id];bool last=i==sequence.Length-1;
                if(elapsed<clip.Duration||last)
                {
                    bool loop=last&&(action is "drag" or "toy-bat"||action.StartsWith("request-")||action.StartsWith("guide-"));
                    int frame=(int)Math.Floor(Math.Max(0,elapsed)*clip.Fps);
                    return new(id,loop?frame%clip.Count:Math.Min(clip.Count-1,frame),clip);
                }
                elapsed-=clip.Duration;
            }
        }
        if(careAction.Length>0)
        {
            string before=careAction;careAction="";current="";group="care-return";
            pose=destination=before is "eat" or "drink" or "toilet" or "bury" or "guide-food" or "guide-water" or "guide-litter"||before.EndsWith("-R")?"SR":before.EndsWith("-L")?"SL":"F";
            if(before=="drag"){careExit="video-34";exitStarted=now;pose=destination="F";}
            else if(before=="toy-bat-R"){careExit="video-31";exitStarted=now;pose=destination="SR";}
        }
        if(careExit.Length>0)
        {
            var clip=clips[careExit];double elapsed=now-exitStarted;
            if(elapsed<clip.Duration)return new(careExit,(int)(Math.Max(0,elapsed)*clip.Fps),clip);
            careExit="";
        }
        return null;
    }
    private void Queue(string clip,bool backwards=false){if(clips.ContainsKey(clip))pending.Enqueue((clip,backwards));}
    private void Begin(double now)
    {
        if(pending.Count==0){current="";return;}
        (current,reverse)=pending.Dequeue();started=now;
    }
    public SpriteFrame? Sample(string action,double now,bool facingLeft=false)
    {
        if(!double.IsFinite(now)||now<0)return null;
        if(careGraph&&SampleCare(action,now,facingLeft) is SpriteFrame care)return care;
        if(videoGraph)return SampleVideo(action,now,facingLeft);
        string next=action switch {
            "idle" or "sit"=>"rest",
            "walk" or "toy-run" or "request-walk" or "guide-walk"=>"move",
            "sleep"=>"sleep", "wake"=>"wake", _=>"other:"+action};
        if(next=="move"&&!facingLeft&&clips.ContainsKey("walk-right"))next="move-right";
        if(next!=group)
        {
            string before=group;group=next;pending.Clear();
            if(next=="rest")
            {
                if(before.StartsWith("move",StringComparison.Ordinal))Queue("move-to-sit");
                if(before.Length>0&&before!="sleep")Queue("sit-to-idle");
                Queue("idle");
            }
            else if(next is "move" or "move-right")Queue(next=="move-right"?"walk-right":"walk");
            else if(next=="sleep")
            {
                if(before.StartsWith("move",StringComparison.Ordinal))Queue("move-to-sit");
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

    private SpriteFrame? SampleVideo(string action,double now,bool left)
    {
        string target=action switch {
            "idle" or "sit" or "wake"=>"F",
            "walk" or "toy-run" or "request-walk" or "guide-walk"=>left?"WL":"WR",
            "sleep"=>"C",_=>""};
        // Interaction wins immediately. Never queue a care/drag action behind a video.
        if(target.Length==0){Reset();return null;}
        if(group.Length==0){pose=destination=target=="C"?"C":"F";group=target;}
        // Waking must not wait for a five-second sleep-entry clip to finish.
        if(action=="wake"&&destination=="C"){pose=destination="C";current="";}
        group=target;
        double nextStarted=now;
        if(current.Length>0)
        {
            var playing=clips[current];
            if(!playing.Loop&&now-started<playing.Duration)
                return new(current,(int)Math.Min(playing.Count-1,Math.Floor((now-started)*playing.Fps)),playing);
            if(!playing.Loop){nextStarted=started+playing.Duration;pose=destination;current="";}
            else if(pose!=target)current="";
        }
        if(current.Length==0)
        {
            if(pose!=target)
            {
                var search=new Queue<(string Node,string First,string End)>();
                var seen=new HashSet<string>{pose};search.Enqueue((pose,"",pose));
                while(search.Count>0)
                {
                    var route=search.Dequeue();
                    if(route.Node==target){current="video-"+route.First;destination=route.End;break;}
                    foreach(var e in edges)if(e.From==route.Node&&clips.ContainsKey("video-"+e.Clip)&&seen.Add(e.To))
                        search.Enqueue((e.To,route.First.Length==0?e.Clip:route.First,route.First.Length==0?e.To:route.End));
                }
            }
            else current=pose switch {"WR"=>"video-right","WL"=>"video-14","C"=>"video-20",_=>"video-01"};
            started=nextStarted;
        }
        if(!clips.TryGetValue(current,out var clip))return null;
        int frame=(int)Math.Floor(Math.Max(0,now-started)*clip.Fps);
        return new(current,clip.Loop?frame%clip.Count:Math.Min(clip.Count-1,frame),clip);
    }

    public double? HorizontalVelocity(string action,double now,bool left)
    {
        if(!videoGraph)return null;
        var frame=Sample(action,now,left);
        if(frame is not SpriteFrame f)return null;
        double t=Math.Clamp((now-started)/f.Definition.Duration,0,1);
        static double Ease(double x){x=Math.Clamp(x,0,1);return x*x*(3-2*x);}
        double velocity=f.Clip switch {
            "video-right"=>36,"video-14"=>-38,
            "video-06"=>12*Ease((t-.18)/.55),"video-08"=>-12*Ease((t-.18)/.55),
            "video-10"=>12+24*Ease(t/.5),"video-12"=>-12-26*Ease(t/.5),
            "video-11"=>36*(1-Ease(t/.85)),"video-13"=>-38*(1-Ease(t/.85)),
            "video-15"=>5*(1-2*Ease(t)),"video-16"=>-5*(1-2*Ease(t)),
            _=>0};
        return velocity*f.Definition.Width/180;
    }
}
