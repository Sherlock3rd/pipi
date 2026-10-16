using Chenpi;
using System.Text.Json;

internal static class ToyGrabChecks
{
    public static void Run(Action<bool,string> check,string path)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(path));var animations=doc.RootElement.GetProperty("animations");
        (PetEngine Cat,SpritePlayback Player) Setup(bool left,bool high)
        {
            var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
            var s=new PetState{X=900,Food=100,Water=100,RestDuration=3600,NestPosition=new(2200,752),FoodPosition=new(1900,752),WaterPosition=new(2050,752),LitterPosition=new(1700,752),FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}};
            var c=new PetEngine(s,7){ExpressionsEnabled=true,CompletionEnabled=true,ToyGrabsEnabled=p.HasToyGrabs,HighToyGrabsEnabled=high,VisualPoseReady=p.PreparePose,VisualStandReady=p.PrepareStand,VisualActionDuration=p.ActionDuration,VisualTravel=p.TravelTo};
            c.Layout(2400,800);p.RestorePose(left?"SL":"SR");return(c,p);
        }
        SpriteFrame Step(PetEngine c,SpritePlayback p){c.Update(1d/24,12);c.Support.Update(c);return p.Sample(c.Action,c.Now,c.FacingLeft)!.Value;}
        foreach(bool left in new[]{false,true})foreach(bool high in new[]{false,true})
        {
            var(c,p)=Setup(left,true);int id=high?(left?154:153):(left?152:151);
            c.SetToy(true,new(c.State.X+(left?-300:300),c.GroundY-(high?130:65)));
            var seen=new HashSet<string>();var frames=new HashSet<int>();bool ran=false,started=false,stable=true;double root=0;
            for(int i=0;i<45*24;i++)
            {
                var f=Step(c,p);seen.Add(f.Clip);ran|=f.Clip==(left?"video-111":"video-108")||f.Clip==(left?"video-112":"video-109");
                if(f.Clip!="video-"+id)continue;
                if(!started){root=c.State.X;started=true;}
                stable&=Math.Abs(root-c.State.X)<.001;
                frames.Add(f.Index);if(frames.Count==97)break;
            }
            check(started&&frames.Count==97&&ran&&stable,$"{id}: independent chase, aligned catch and all 97 original frames [started={started} frames={frames.Count} ran={ran} stable={stable} x={c.State.X} action={c.Action} seen={string.Join(",",seen)}]");
            check(Math.Abs(c.ToyTip.X-c.State.X-c.ToyReachOffset)<=12,$"{id}: stop distance uses actual reaching paw");
            // Change both side and height during a fresh complete catch.
            for(int i=0;i<24*10&&(c.ActionTime>1||c.ToyGrabClip!=id);i++)Step(c,p);
            c.SetToy(true,new(c.State.X+(left?147:-149),c.GroundY-(high?65:130)));
            bool held=true;int last=-1;
            for(int i=0;i<24*10&&c.ToyGrabClip==id;i++)
            {
                var f=Step(c,p);if(c.ToyGrabClip==id){held&=c.FacingLeft==left&&f.Clip=="video-"+id;last=Math.Max(last,f.Index);}
            }
            check(held&&last==96,$"{id}: moving feather across side/height finishes the current landing");
            int next=high?(left?151:152):(left?153:154);bool turned=false,reached=false;
            for(int i=0;i<50*24;i++){var f=Step(c,p);turned|=f.Clip==(left?"video-16":"video-15");if(f.Clip=="video-"+next){reached=true;break;}}
            check(turned&&reached,$"{id}: real turn precedes opposite-side height-selected catch");
            c.SetToy(false,c.ToyTip);bool finished=false;
            for(int i=0;i<8*24;i++){var f=Step(c,p);finished|=f.Clip=="video-"+next&&f.Index==96;}
            check(finished&&c.ToyGrabClip==0&&!c.ToyHeld,$"{id}: return wand completes catch then stops");
        }
        var(gate,gp)=Setup(false,false);gate.SetToy(true,new(1049,600));
        for(int i=0;i<24*8;i++)Step(gate,gp);
        check(gate.ToyGrabClip==151&&!gate.WantsHighToyGrab,"unapproved high clips cannot enter the runtime branch");
        gate.BeginDrag();Step(gate,gp);check(gate.Action=="drag","direct drag retains input priority during a catch");
    }
}
