using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chenpi;

public sealed record SpriteClip(int Count,double Fps,bool Loop,double Width=180,double Height=180,double AnchorX=.5,double AnchorY=.921875,bool MirrorWithFacing=true,double ScaleStart=1,double ScaleEnd=1,double OffsetStartX=0,double OffsetStartY=0,double OffsetEndX=0,double OffsetEndY=0,double PlaybackRate=1,[property:JsonIgnore] double[]? GroundContacts=null,double BuryPlaybackRate=1,double? LandingContactY=null,[property:JsonIgnore] double[][]? HorizontalRegistration=null,double EatDrinkPlaybackRate=1)
{
    public double Duration=>Count/Fps;
    public SpriteClip ForAction(string action)
    {
        double rate=action switch {"bury"=>BuryPlaybackRate,"eat" or "drink"=>EatDrinkPlaybackRate,_=>1};
        return rate!=1?this with {Fps=Fps*rate,PlaybackRate=PlaybackRate*rate}:this;
    }
    public SpriteClip OnGround(int index)=>GroundContacts is not null&&index>=0&&index<GroundContacts.Length?this with {AnchorY=GroundContacts[index]}:this;
    public SpriteClip AtFrame(int index)
    {
        double t=Math.Clamp(index/(double)Math.Max(1,Count-1),0,1);t=t*t*(3-2*t);
        double scale=ScaleStart+(ScaleEnd-ScaleStart)*t;
        double ox=OffsetStartX+(OffsetEndX-OffsetStartX)*t,oy=OffsetStartY+(OffsetEndY-OffsetStartY)*t;
        var result=this with {Width=Width*scale,Height=Height*scale,AnchorX=AnchorX-ox/(Width*scale),AnchorY=AnchorY-oy/(Height*scale),ScaleStart=1,ScaleEnd=1,OffsetStartX=0,OffsetStartY=0,OffsetEndX=0,OffsetEndY=0};
        if(HorizontalRegistration is not null&&index>=0&&index<HorizontalRegistration.Length)
        {
            // Invert measured source-frame horizontal stretch; keep the support
            // height and original vertical pose completely untouched.
            var correction=HorizontalRegistration[index];double width=result.Width*correction[0];
            result=result with {Width=width,AnchorX=result.AnchorX-correction[1]/width};
        }
        if(LandingContactY is double contact)
        {
            // Preserve the airborne descent. Only the seated final fifth joins
            // the grounded idle anchor; neither body scale nor timing changes.
            double settle=Math.Clamp((index/(double)Math.Max(1,Count-1)-.8)/.2,0,1);
            settle=settle*settle*(3-2*settle);
            result=result with {AnchorY=result.AnchorY+(contact-result.AnchorY)*settle};
        }
        return result.OnGround(index);
    }
}
public readonly record struct SpriteFrame(string Clip,int Index,SpriteClip Definition);

// Visual transitions never delay gameplay, input, care events or waking the cat.
public sealed partial class SpritePlayback
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
    private double requestFinishAt=-1;
    private readonly List<(double Scale,double X,double Y)> sleepPhases=new();
    private SpriteFrame? lastSample;
    private double wakeContinuationAt=-1,wakeScale=1,wakeX,wakeY;
    private string pose="F",destination="F";
    private static readonly (string From,string To,string Clip)[] edges={
        ("F","SR","06"),("SR","F","07"),("F","SL","08"),("SL","F","09"),
        ("SR","WR","10"),("WR","SR","11"),("SL","WL","12"),("WL","SL","13"),
        ("SR","SL","15"),("SL","SR","16"),("F","I","17"),("I","F","18"),
        ("I","C","19"),("C","I","21")};
    public bool HasFoundation=>clips.ContainsKey("idle")&&clips.ContainsKey("walk")&&clips.ContainsKey("sleep");

    public void Load(JsonElement manifest,Func<string,int> frameCount)
    {
        clips.Clear();sleepPhases.Clear();Reset();
        if(!manifest.TryGetProperty("clips",out var entries)||entries.ValueKind!=JsonValueKind.Object)return;
        foreach(var item in entries.EnumerateObject())
        {
            try
            {
                var value=item.Value;int count=frameCount(item.Name);
                double fps=value.GetProperty("fps").GetDouble();
                double rate=PlaybackSpeed(value);
                bool loop=value.GetProperty("loop").GetBoolean();
                double width=value.TryGetProperty("width",out var w)?w.GetDouble():180;
                double height=value.TryGetProperty("height",out var h)?h.GetDouble():180;
                double ax=value.TryGetProperty("anchorX",out var x)?x.GetDouble():.5;
                double ay=value.TryGetProperty("anchorY",out var y)?y.GetDouble():.921875;
                if(count>0&&double.IsFinite(fps)&&fps>=1&&fps<=60&&double.IsFinite(width)&&width>0&&width<=512
                    &&double.IsFinite(height)&&height>0&&height<=512&&double.IsFinite(ax)&&ax>=0&&ax<=1&&double.IsFinite(ay)&&ay>=0&&ay<=1)
                    clips[item.Name]=new(count,fps*rate,loop,width,height,ax,ay,
                        !value.TryGetProperty("mirrorWithFacing",out var mirror)||mirror.GetBoolean(),
                        ReadScale(value,"scaleStart"),ReadScale(value,"scaleEnd"),
                        ReadOffset(value,"offsetStartX"),ReadOffset(value,"offsetStartY"),ReadOffset(value,"offsetEndX"),ReadOffset(value,"offsetEndY"),rate,ReadGroundContacts(value,count),PlaybackSpeed(value,"buryPlaybackRate"),ReadLandingContact(value),ReadHorizontalRegistration(value,count),PlaybackSpeed(value,"eatDrinkPlaybackRate"));
            }
            catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException){ }
        }
        videoGraph=manifest.TryGetProperty("videoGraph",out var graph)&&graph.GetBoolean()
            &&clips.ContainsKey("video-01")&&clips.ContainsKey("video-14")&&clips.ContainsKey("video-right");
        careGraph=manifest.TryGetProperty("careVideoGraph",out var care)&&care.GetBoolean()&&clips.ContainsKey("video-22");
        if(entries.TryGetProperty("video-20",out var sleeping)&&sleeping.TryGetProperty("phaseRegistration",out var phases)&&phases.ValueKind==JsonValueKind.Array)
            foreach(var phase in phases.EnumerateArray())
                if(phase.ValueKind==JsonValueKind.Array&&phase.GetArrayLength()==3&&phase[0].TryGetDouble(out double s)&&phase[1].TryGetDouble(out double px)&&phase[2].TryGetDouble(out double py)
                    &&double.IsFinite(s)&&s>=.9&&s<=1.1&&double.IsFinite(px)&&Math.Abs(px)<=20&&double.IsFinite(py)&&Math.Abs(py)<=20)sleepPhases.Add((s,px,py));
    }
    private static double ReadScale(JsonElement value,string key)=>value.TryGetProperty(key,out var number)&&number.TryGetDouble(out double n)&&double.IsFinite(n)&&n>=.9&&n<=1.1?n:1;
    public SpriteFrame? InspectFrame(string id,int index)=>clips.TryGetValue(id,out var clip)&&index>=0&&index<clip.Count?new(id,index,clip.AtFrame(index)):null;
    public static double[][]? ReadHorizontalRegistration(JsonElement value,int count)
    {
        if(!value.TryGetProperty("horizontalRegistration",out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()!=count)return null;
        var result=new double[count][];int i=0;
        foreach(var item in list.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Array||item.GetArrayLength()!=2||!item[0].TryGetDouble(out double scale)||!item[1].TryGetDouble(out double offset)||!double.IsFinite(scale)||scale<.95||scale>1.05||!double.IsFinite(offset)||Math.Abs(offset)>12)return null;
            result[i++]=new[]{scale,offset};
        }
        return result;
    }
    private static double? ReadLandingContact(JsonElement value)=>value.TryGetProperty("landingContactY",out var number)&&number.TryGetDouble(out double n)&&double.IsFinite(n)&&n>=.5&&n<=1?n:null;
    public static double[]? ReadGroundContacts(JsonElement value,int count)
    {
        if(!value.TryGetProperty("groundContacts",out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()!=count)return null;
        var contacts=new double[count];int i=0;
        foreach(var item in list.EnumerateArray())
        {if(!item.TryGetDouble(out double n)||!double.IsFinite(n)||n<.5||n>1)return null;contacts[i++]=n;}
        return contacts;
    }
    public static double PlaybackSpeed(JsonElement value,string key="playbackRate")=>value.TryGetProperty(key,out var number)&&number.TryGetDouble(out double n)&&double.IsFinite(n)&&n>=.5&&n<=4?n:1;
    private static double ReadOffset(JsonElement value,string key)=>value.TryGetProperty(key,out var number)&&number.TryGetDouble(out double n)&&double.IsFinite(n)&&Math.Abs(n)<=20?n:0;
    public void Reset(){travel=null;group="";current="";careAction=careExit="";requestFinishAt=-1;pending.Clear();started=0;reverse=false;pose=destination="F";lastSample=null;wakeContinuationAt=-1;carriedExpression=null;}
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
        if(carriedExpression is not null&&action=="land")return .7;
        if(HasExpressions&&action.StartsWith("expr-")&&clips.TryGetValue("video-"+action[5..],out var expression))return expression.Duration;
        if(!careGraph)return null;var seq=CareSequence(action,left);if(seq.Length==0)return null;
        double sum=0;foreach(var id in seq){if(!clips.TryGetValue("video-"+id,out var clip))return null;sum+=clip.ForAction(action).Duration;}return sum;
    }
    public (double Start,double Duration)? ConsumptionWindow(string action)
    {
        if(!careGraph||action is not ("eat" or "drink"))return null;
        return(clips["video-22"].ForAction(action).Duration,clips[action=="eat"?"video-23":"video-25"].ForAction(action).Duration);
    }
    public (double Start,double Duration)? BurialWindow()=>careGraph?
        (clips["video-29"].ForAction("bury").Duration,clips["video-30"].Duration):null;
    private SpriteFrame? SampleCare(string action,double now,bool left)
    {
        if(action!="care-finish")requestFinishAt=-1;
        else if(careAction is "request-food" or "request-water" or "request-litter" or "guide-food" or "guide-water" or "guide-litter")
        {
            string id="video-"+CareSequence(careAction,left)[0];var finishing=clips[id];
            // Refill changes stock immediately, but finish the currently visible
            // gesture at its authored endpoint before walking through the graph.
            if(requestFinishAt<0)requestFinishAt=careStarted+Math.Max(1,Math.Ceiling((now-careStarted)/finishing.Duration-1e-8))*finishing.Duration;
            if(now<requestFinishAt-1e-8)
                return new(id,FrameIndex(now-careStarted,finishing.Fps)%finishing.Count,finishing);
        }
        var sequence=HasExpressions&&action.StartsWith("expr-")&&clips.ContainsKey("video-"+action[5..])?new[]{action[5..]}:CareSequence(action,left);
        if(sequence.Length>0)
        {
            string key=action+(action is "toy-bat" or "guide-look"?(left?"-L":"-R"):"");
            if(key!=careAction){careAction=key;careStarted=now;careExit="";current="";group="care";pending.Clear();}
            double elapsed=now-careStarted;
            for(int i=0;i<sequence.Length;i++)
            {
                string id="video-"+sequence[i];var clip=clips[id].ForAction(action);bool last=i==sequence.Length-1;
                if(elapsed<clip.Duration||last)
                {
                    bool loop=last&&(action is "drag" or "toy-bat"||action.StartsWith("request-")||action.StartsWith("guide-"));
                    int frame=FrameIndex(elapsed,clip.Fps);
                    return new(id,loop?frame%clip.Count:Math.Min(clip.Count-1,frame),clip);
                }
                elapsed-=clip.Duration;
            }
        }
        if(careAction.Length>0)
        {
            string before=careAction;careAction="";current="";group="care-return";
            pose=destination=before.StartsWith("expr-")&&int.TryParse(before[5..],out int expressionId)?ExpressionPose(expressionId):before is "eat" or "drink" or "toilet" or "bury" or "guide-food" or "guide-water" or "guide-litter"||before.EndsWith("-R")?"SR":before.EndsWith("-L")?"SL":"F";
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
    // Stabilize exact frame boundaries (e.g. a 24 Hz audit) against binary
    // rounding; this does not change frame duration or skip an authored frame.
    private static int FrameIndex(double elapsed,double fps)=>(int)Math.Floor(Math.Max(0,elapsed)*fps+1e-8);
    private void Begin(double now)
    {
        if(pending.Count==0){current="";return;}
        (current,reverse)=pending.Dequeue();started=now;
    }
    public SpriteFrame? Sample(string action,double now,bool facingLeft=false)
    {
        var sample=SampleCore(action,now,facingLeft);
        if(sample is not SpriteFrame frame)return null;
        var definition=frame.Definition.AtFrame(frame.Index);
        if(lastSample is SpriteFrame old&&old.Clip=="video-20"&&frame.Clip=="video-21"&&old.Index<sleepPhases.Count)
        {
            var phase=sleepPhases[old.Index];wakeScale=1/phase.Scale;wakeX=-phase.X/phase.Scale;wakeY=-phase.Y/phase.Scale;wakeContinuationAt=now;
        }
        if(frame.Clip=="video-21"&&wakeContinuationAt>=0)
        {
            double t=Math.Clamp((now-wakeContinuationAt)/.5,0,1),weight=1-t*t*(3-2*t);
            double scale=1+(wakeScale-1)*weight,w=definition.Width*scale,h=definition.Height*scale;
            definition=definition with {Width=w,Height=h,AnchorX=definition.AnchorX-wakeX*weight/w,AnchorY=definition.AnchorY-wakeY*weight/h};
        }
        else if(frame.Clip!="video-20")wakeContinuationAt=-1;
        definition=definition.OnGround(frame.Index);
        lastSample=frame;
        return frame with {Definition=definition};
    }
    private SpriteFrame? SampleCore(string action,double now,bool facingLeft=false)
    {
        if(!double.IsFinite(now)||now<0)return null;
        if(SampleCarriedExpression(action,now) is SpriteFrame carried)return carried;
        if(travel is not null)
        {
            if(action==travel.Action)return TravelFrame(now).Frame;
            CancelTravel(now);
        }
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
            "idle" or "sit" or "wake" or "care-finish"=>"F",
            "walk" or "toy-run" or "request-walk" or "guide-walk"=>left?"WL":"WR",
            "guide-stop" or "guide-arrive" or "toy-ready"=>left?"SL":"SR",
            "sleep"=>"C",_=>""};
        if(action=="care-ready")target="SR";
        if(HasExpressions&&action.StartsWith("rest-"))target=action[5..];
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
                return new(current,Math.Min(playing.Count-1,FrameIndex(now-started,playing.Fps)),playing);
            if(!playing.Loop){nextStarted=started+playing.Duration;pose=destination;current="";}
            else if(pose!=target)current="";
        }
        if(current.Length==0)
        {
            if(pose==target&&pose is "SR" or "SL")
            {
                string stand=pose=="SL"?"video-08":"video-06";
                return new(stand,clips[stand].Count-1,clips[stand]);
            }
            if(pose!=target)
            {
                var search=new Queue<(string Node,string First,string End)>();
                var seen=new HashSet<string>{pose};search.Enqueue((pose,"",pose));
                while(search.Count>0)
                {
                    var route=search.Dequeue();
                    if(route.Node==target){current="video-"+route.First;destination=route.End;break;}
                    foreach(var e in GraphEdges())if(e.From==route.Node&&clips.ContainsKey("video-"+e.Clip)&&seen.Add(e.To))
                        search.Enqueue((e.To,route.First.Length==0?e.Clip:route.First,route.First.Length==0?e.To:route.End));
                }
            }
            else current=PoseLoop(pose) is string loop&&loop.Length>0?"video-"+loop:pose switch {"WR" or "WL"=>WalkingLoop(pose),"C"=>"video-20",_=>"video-01"};
            started=nextStarted;
        }
        if(!clips.TryGetValue(current,out var clip))return null;
        int frame=FrameIndex(now-started,clip.Fps);
        return new(current,clip.Loop?frame%clip.Count:Math.Min(clip.Count-1,frame),clip);
    }
    public bool PrepareCare(double now)
    {
        if(!careGraph)return true;
        Sample("care-ready",now,false);
        return pose=="SR"&&destination=="SR"&&current.Length==0;
    }
    public bool PrepareRequestFinish(double now)
    {
        if(!careGraph)return true;
        Sample("care-finish",now);
        return careAction.Length==0&&pose=="F"&&destination=="F"&&(current.Length==0||clips[current].Loop);
    }
    public bool PrepareStand(double now,bool left)
    {
        if(!videoGraph)return true;
        Sample("guide-stop",now,left);
        string stand=left?"SL":"SR";
        return pose==stand&&destination==stand&&current.Length==0;
    }

    public double? HorizontalVelocity(string action,double now,bool left)
    {
        if(!videoGraph)return null;
        var frame=Sample(action,now,left);
        if(frame is not SpriteFrame f)return null;
        double t=Math.Clamp((now-started)/f.Definition.Duration,0,1);
        return FrameVelocity(f.Clip,t);
    }
    private double FrameVelocity(string id,double t)
    {
        static double Ease(double x){x=Math.Clamp(x,0,1);return x*x*(3-2*x);}
        double velocity=id switch {
            "video-right" or "video-86"=>36,"video-14" or "video-87"=>-38,
            "video-06"=>12*Ease((t-.18)/.55),"video-08"=>-12*Ease((t-.18)/.55),
            "video-10"=>12+24*Ease(t/.5),"video-12"=>-12-26*Ease(t/.5),
            "video-11"=>36*(1-Ease(t/.85)),"video-13"=>-38*(1-Ease(t/.85)),
            "video-15"=>5*(1-2*Ease(t)),"video-16"=>-5*(1-2*Ease(t)),
            _=>0};
        return velocity*clips[id].Width/(id is "video-86" or "video-87"?225:180)*clips[id].PlaybackRate;
    }
}
