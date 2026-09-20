using Chenpi;
using System.Text.Json;

public static class CompletionChecks
{
    public static void Run(Action<bool,string> check,string manifestPath)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(manifestPath));var animations=doc.RootElement.GetProperty("animations");
        SpritePlayback Player(){var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);return p;}
        var ready=Player();check(ready.HasCompletion&&Enumerable.Range(99,41).All(id=>ready.InspectFrame("video-"+id,0) is not null),"all 41 completion clips loaded");
        var incomplete=new SpritePlayback();incomplete.Load(doc.RootElement,id=>id=="video-108"?0:animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
        check(!incomplete.HasCompletion,"missing run-start clip gates the completion behavior graph");
        foreach(int n in Enumerable.Range(125,15))
        {
            int count=animations.GetProperty("video-"+n).GetArrayLength();
            var lifts=Enumerable.Range(0,count).Select(i=>SpritePlayback.AuthoredCarryLift(ready.InspectFrame("video-"+n,i)!.Value)!.Value).ToArray();
            check(lifts.All(v=>double.IsFinite(v)&&v>=0&&v<=24),"finite continuous carry height "+n);
            int phase=(n-125)%3;
            check(Math.Abs(lifts[0]-(phase==0?0:24))<.001&&Math.Abs(lifts[^1]-(phase==2?0:24))<.001,"carry joins supported endpoints "+n);
        }
        PetEngine Cat(SpritePlayback p,bool nestRight=false)
        {
            var state=new PetState{X=900,Y=752,Food=80,Water=60,Litter=20,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}};
            var e=new PetEngine(state,7){ExpressionsEnabled=true,CompletionEnabled=true,VisualPoseReady=p.PreparePose,VisualRestorePose=p.RestorePose,VisualDropPose=()=>p.DropPose,VisualTravel=p.TravelTo,VisualActionDuration=p.ActionDuration,VisualVelocity=p.HorizontalVelocity};
            e.Layout(3000,800);e.MoveObject("nest",new(nestRight?2700:250,752));e.MoveObject("food",new(2100,752));e.MoveObject("water",new(2300,752));e.MoveObject("litter",new(2500,752));
            return e;
        }
        SpriteFrame Tick(PetEngine e,SpritePlayback p){e.Update(1d/24,12);e.Support.Update(e);return p.Sample(e.Action,e.Now,e.FacingLeft)!.Value;}
        foreach(var (from,to,id) in new[]{("I","SR",99),("I","SL",100),("SR","D",101),("SL","D",102),("D","SR",103),("D","SL",104),("A","SL",105),("B","SL",106),("X","SL",107),("C","D",121),("D","C",122),("C","A",123),("A","C",124)})
        {var p=Player();p.RestorePose(from);check(p.Sample("rest-"+to,0)!.Value.Clip=="video-"+id,$"direct route {from} to {to} uses {id}");}
        foreach(bool right in new[]{false,true})
        {
            var p=Player();var e=Cat(p,right);double[] supplies={e.State.Food,e.State.Water,e.State.Litter,e.State.FoodClock.NextDue};
            check(e.BeginStartup(firstRun:true,boot:"boot-A"),"eligible first run starts "+right);
            var seen=new HashSet<string>();double step=0,previous=e.VisualPosition.X;int count=0;
            for(;count<24*240&&e.StartupActive;count++)
            {
                var f=Tick(e,p);seen.Add(f.Clip);step=Math.Max(step,Math.Abs(e.VisualPosition.X-previous));previous=e.VisualPosition.X;
                if(count==100){double time=e.Now,actionTime=e.ActionTime,x=e.State.X;e.PresentationPaused=true;for(int i=0;i<120;i++)e.Update(1d/24,12);check(e.Now==time&&e.ActionTime==actionTime&&e.State.X==x,"hidden startup freezes all presentation clocks "+right);e.PresentationPaused=false;}
            }
            check(!e.StartupActive&&e.Action=="idle"&&seen.Contains("video-116")&&seen.Contains("video-117")&&seen.Contains("video-118"),"full startup returns only after front sit "+right);
            check(seen.Contains(right?"video-112":"video-109")&&seen.Contains(right?"video-100":"video-99")&&!seen.Contains("video-18"),"authored run and direct wake route "+right);
            check(step<20&&Math.Abs(e.Support.Height)<.01&&e.IsClearRestSpot(e.State.X),"continuous root and supported departure to clear center "+right);
            check(supplies.SequenceEqual(new[]{e.State.Food,e.State.Water,e.State.Litter,e.State.FoodClock.NextDue}),"greeting preserves care stock and deadlines "+right);
            check(!e.BeginStartup(firstRun:true,autoStart:true,boot:"boot-A")&&!PetEngine.StartupEligible(e.State,false,false,"boot-B")&&PetEngine.StartupEligible(e.State,false,true,"boot-B"),"persistent greeting dedup distinguishes normal reopen and next boot");
            Console.WriteLine("Startup "+right+" seconds="+count/24d+" clips="+string.Join(',',seen));
        }
        foreach(string basePose in new[]{"D","A","B","X","M"})
        foreach(bool early in new[]{false,true})
        {
            var p=Player();var e=Cat(p);e.PreviewRest(basePose);p.RestorePose(basePose);Tick(e,p);e.BeginDrag();var seen=new HashSet<string>();
            for(int i=0;i<(early?24:240);i++)seen.Add(Tick(e,p).Clip);
            e.Drop(false);for(int i=0;i<500&&e.Action=="land";i++)seen.Add(Tick(e,p).Clip);
            check(e.Action=="rest-"+basePose&&seen.Any(s=>int.TryParse(s[6..],out int n)&&n>=125)&&!seen.Contains("video-32")&&!seen.Contains("video-34"),"authored pickup/release returns to "+basePose+" early="+early);
        }
        foreach(int stage in new[]{0,1,2,5,6,7,8})
        {
            var p=Player();var e=Cat(p);e.BeginStartup(preview:true);
            for(int i=0;i<24*240&&e.StartupStage<stage;i++)Tick(e,p);
            e.Interact();for(int i=0;i<24*40&&e.StartupActive;i++)Tick(e,p);
            check(!e.StartupActive&&e.Action=="pet","click cancels startup with a legal seated transition at stage "+stage);
        }
        check(!PetEngine.StartupEligible(new PetState(),false,false,"boot"),"legacy save is never inferred to be first run");
        {
            var p=Player();var e=Cat(p);e.BeginStartup(preview:true);
            for(int i=0;i<24*60&&e.StartupStage<6;i++)Tick(e,p);
            e.BeginDrag();for(int i=0;i<24*40&&e.StartupActive;i++)Tick(e,p);
            check(!e.StartupActive&&e.Action=="drag","drag cancels startup and reaches pickup after valid transition");
            e.Drop(false);for(int i=0;i<24*30&&e.Action=="land";i++)Tick(e,p);
            check(!e.Holding&&e.Action!="land","cancelled startup drag can land normally");
        }
        {
            var p=Player();var e=Cat(p);e.ExecuteBehavior("pose-C");
            check(e.DebugBehaviorActive,"workbench can execute newly connected curled sleep");
        }
        {
            var p=Player();var e=Cat(p);e.BeginStartup(preview:true);
            for(int i=0;i<24*60&&e.StartupStage<6;i++)Tick(e,p);
            e.BeginDrag();Tick(e,p);e.Drop(false);var seen=new HashSet<string>();
            for(int i=0;i<24*40&&e.StartupActive;i++)seen.Add(Tick(e,p).Clip);
            check(!e.StartupActive&&e.Action=="idle"&&!seen.Contains("video-34"),"release during greeting cancellation does not invent a pickup landing");
        }
    }
}
