using Chenpi;
using System.Text.Json;

public static class ExpressionChecks
{
    public static void Run(Action<bool,string> check,string manifestPath)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(manifestPath));var animations=doc.RootElement.GetProperty("animations");
        SpritePlayback Player(){var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);return p;}
        var ready=Player();check(ready.HasExpressions,"all 47 returned expression clips are available");
        PetEngine Cat(SpritePlayback p,int seed=7)
        {
            var e=new PetEngine(new PetState{X=300,Y=752,Food=100,Water=100,Litter=0,RestDuration=3600,
                NestPosition=new(1800,752),FoodPosition=new(1100,752),WaterPosition=new(1350,752),LitterPosition=new(1550,752),
                FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}},seed);
            e.Layout(2200,800);e.ExpressionsEnabled=true;e.VisualPoseReady=p.PreparePose;e.VisualWallRestComplete=p.WallRestComplete;e.VisualTravel=p.TravelTo;e.VisualVelocity=p.HorizontalVelocity;
            e.VisualCareReady=p.PrepareCare;e.VisualActionDuration=p.ActionDuration;e.VisualConsumptionWindow=p.ConsumptionWindow;return e;
        }
        void Tick(PetEngine c,SpritePlayback p,double seconds,HashSet<string>? seen=null)
        {for(int i=0;i<(int)Math.Ceiling(seconds/.05);i++){c.Update(.05,12);var f=p.Sample(c.AligningForCare?"care-ready":c.Action,c.Now,c.FacingLeft);if(f is {} frame)seen?.Add(frame.Clip);}}
        foreach(var (pose,clip) in new[]{("HL","video-56"),("HR","video-53")})
        {
            var p=Player();p.RestorePose(pose);p.Sample("rest-"+pose,0);double cycle=p.InspectFrame(clip,0)!.Value.Definition.Duration;
            check(!p.WallRestComplete(pose,cycle*3.2,cycle*3.1)&&p.WallRestComplete(pose,cycle*4,cycle*3.1),"repeated wall command waits for next complete loop "+pose);
        }
        foreach(bool left in new[]{true,false})
        {
            var p=Player();var c=Cat(p);c.Layout(3000,800);c.State.X=400;
            c.WallRightOffset=doc.RootElement.GetProperty("wallContactOffsets").GetProperty("right").GetDouble();
            c.WallLeftOffset=doc.RootElement.GetProperty("wallContactOffsets").GetProperty("left").GetDouble();
            var before=c.State.X;var seen=new HashSet<string>();c.ExecuteBehavior(left?"wall-left":"wall-right");
            check(c.State.X==before&&c.Action=="walk","workbench wall command travels without teleport "+left);
            bool completed=false;double maxStep=0,priorX=c.State.X;
            for(int i=0;i<8000;i++)
            {
                Tick(c,p,.05,seen);maxStep=Math.Max(maxStep,Math.Abs(c.State.X-priorX));priorX=c.State.X;
                if(seen.Contains(left?"video-57":"video-54")&&!c.DebugBehaviorActive&&c.Action!="walk"){completed=true;break;}
            }
            check(completed&&seen.Contains(left?"video-55":"video-52")&&seen.Contains(left?"video-56":"video-53"),"workbench wall includes enter, loop, land and returns to tree "+left);
            check(maxStep<20&&Math.Abs(c.State.Y-c.GroundY)<.001,"wall route keeps root continuous and ground support "+left);
            Console.WriteLine("Wall preview "+left+": max root step "+maxStep.ToString("F4")+"; clips "+string.Join(",",seen));
        }
        {
            var p=Player();var c=Cat(p);c.MoveObject("nest",new(70,c.GroundY));long revision=c.ActionRevision;
            check(c.ExecuteBehavior("wall-left").Contains("家具")&&c.ActionRevision==revision,"blocked wall is rejected before changing current action");
            check(c.ExecuteBehavior("intro-run").Contains("等待")&&c.ActionRevision==revision,"missing startup never uses walking substitute");
            c.State.Food=0;check(c.ExecuteBehavior("food").Contains("为空")&&c.State.Food==0&&c.ActionRevision==revision,"debug eating does not create stock");
        }
        {
            var p=Player();var c=Cat(p);var seen=new HashSet<string>();c.ExecuteBehavior("expr-80");Tick(c,p,45,seen);
            check(seen.Contains("video-58")&&seen.Contains("video-60")&&seen.Contains("video-80")&&c.State.Sleeping&&!c.DebugBehaviorActive,"selected gesture prepares its pose, completes and resumes sleep");
            c.ExecuteBehavior("wall-left");c.BeginDrag();Tick(c,p,1);check(!c.DebugBehaviorActive&&c.Action=="drag","drag cancels workbench action immediately");
            c.Drop(false);c.ExecuteBehavior("pose-A");c.SetToy(true,new(c.State.X,c.State.Y));check(!c.DebugBehaviorActive,"toy pickup cancels workbench pose priority");
            c.SetToy(false,new());c.ExecuteBehavior("pose-B");c.Interact();check(!c.DebugBehaviorActive,"click cancels workbench priority");
        }
        foreach(bool left in new[]{true,false})
        {
            var p=Player();var c=Cat(p);c.Layout(3000,800);c.State.X=left?70:2930;c.State.StillSeconds=31;
            var seen=new HashSet<string>();Tick(c,p,35,seen);
            check(seen.Contains(left?"video-56":"video-53"),"autonomous wall triggers at eligible clear edge "+left);
        }
        foreach(var (pose,loop,first,second,exit) in new[]{("A","67","68","69","70"),("B","71","72","73","74"),("X","75","76","77","78")})
        {
            var p=Player();var c=Cat(p);var seen=new HashSet<string>();c.PreviewRest(pose);Tick(c,p,30,seen);
            check(seen.Contains("video-58")&&seen.Contains("video-60")&&seen.Contains("video-"+loop)&&c.State.Sleeping,"ground rest reaches "+pose+" through authored transitions and sleeps");
            seen.Clear();c.Interact();Tick(c,p,.1,seen);check(c.Action=="expr-"+first&&c.RelaxedClickCount==1,pose+" first click reacts immediately");
            c.Interact();Tick(c,p,4.5,seen);check(seen.Contains("video-"+second)&&c.RelaxedClickCount==2,pose+" second click follows the completed first reaction");
            c.Interact();Tick(c,p,20,seen);check(seen.Contains("video-"+exit)&&c.RelaxedClickCount==0,pose+" third click uses its direct standing exit and resets count");
            p=Player();c=Cat(p);c.PreviewRest(pose);Tick(c,p,30);c.Interact();c.Interact();c.Interact();seen.Clear();Tick(c,p,12,seen);
            check(!seen.Contains("video-"+second)&&seen.Contains("video-"+exit),pose+" rapid triple click coalesces without queueing obsolete second-tier reactions");
            p=Player();c=Cat(p);c.PreviewRest(pose);Tick(c,p,30);c.Interact();Tick(c,p,15);c.Interact();check(c.RelaxedClickCount==1,pose+" 15-second boundary opens a new fixed click window");
        }
        {
            var p=Player();var c=Cat(p);var seen=new HashSet<string>();c.PreviewRest("M");Tick(c,p,30,seen);c.Interact();Tick(c,p,.1,seen);
            check(c.Action=="rest-A"&&!c.State.Sleeping&&seen.Contains("video-85"),"masked sleep wakes immediately through lowering paws, without a triple click");
            c.State.FoodClock.NextDue=0;Tick(c,p,70,seen);
            check(seen.Contains("video-70")&&seen.Contains("video-22")&&c.State.Food<100,"masked waking plus overdue care uses A-to-standing and actually eats");
        }
        foreach(string pose in new[]{"D","A","B","X","M","HR","HL"})
        {
            var p=Player();p.RestorePose(pose);var before=p.Sample("rest-"+pose,0)!.Value;
            var carried=p.Sample("drag",.1)!.Value;var dropped=p.Sample("land",1)!.Value;
            check(before.Clip==carried.Clip&&before.Index==carried.Index&&dropped.Clip==carried.Clip,"drag/drop keeps actual "+pose+" art instead of snapping to seated pickup");
        }
        {
            var p=Player();var c=Cat(p);var seen=new HashSet<string>();c.Demo("eat");bool meal=false;var post=new List<string>();
            for(int i=0;i<4000;i++){c.Update(.05,12);var f=p.Sample(c.AligningForCare?"care-ready":c.Action,c.Now,c.FacingLeft)!.Value;meal|=f.Clip=="video-24";if(meal&&(post.Count==0||post[^1]!=f.Clip))post.Add(f.Clip);if(meal&&post.Contains("video-13"))break;}
            check(post.Count>=4&&post[0]=="video-24"&&post[1]=="video-15"&&post[2]=="video-12"&&!post.Take(3).Contains("video-07"),"meal exit turns left directly from standing without sitting first");
        }
        {
            var p=Player();var c=Cat(p);int resting=0,moving=0;var seen=new HashSet<string>();
            for(int i=0;i<36000;i++){c.Update(.1,12);p.Sample(c.Action,c.Now,c.FacingLeft);if(c.State.Sleeping)resting++;if(c.Action=="walk")moving++;seen.Add(c.Action);}
            check(resting>30000&&moving<1000&&seen.Count(a=>a.StartsWith("rest-"))>=3,"one-hour supplied idle simulation spends over 83 percent sleeping and visits multiple lying poses without roaming");
        }
        foreach(bool left in new[]{false,true})
        {
            var p=Player();var all=new HashSet<string>();
            for(int leg=0;leg<3;leg++)
            {
                p.RestorePose(left?"SL":"SR");double x=left?1900:200,to=left?200:1900;
                for(double t=0;t<150;t+=.025)
                {var move=p.TravelTo("walk",x,to,leg*200+t,left)!.Value;x=move.X;all.Add(p.Sample("walk",leg*200+t,left)!.Value.Clip);if(move.Complete)break;}
                check(Math.Abs(x-to)<.01,"long voiced travel reaches destination without overshoot "+left+" leg "+leg);
            }
            check(all.Contains(left?"video-87":"video-86"),"long travel occasionally inserts the full directional walking call "+left);
        }
        {
            var p=Player();var c=Cat(p);c.State.FoodClock.NextDue=1200;c.State.WaterClock.NextDue=600;c.State.LitterClock.NextDue=3600;
            int sleepingFrames=0,eats=0,drinks=0;string prior="";
            for(int i=0;i<36000;i++)
            {
                c.AdvanceNeeds(.1);c.Update(.1,12);var f=p.Sample(c.AligningForCare?"care-ready":c.Action,c.Now,c.FacingLeft)!.Value;
                if(f.Clip is "video-66" or "video-67" or "video-71" or "video-75" or "video-84")sleepingFrames++;
                if(c.Action!=prior){if(c.Action=="eat")eats++;if(c.Action=="drink")drinks++;}prior=c.Action;
                if(c.State.Food<=20)c.Refill("food");if(c.State.Water<=20)c.Refill("water");
            }
            Console.WriteLine($"Expression care simulation: breathing poses {sleepingFrames/360d:F1}%, meals {eats}, drinks {drinks}");
            check(sleepingFrames>25200&&eats>0&&drinks>0,"one-hour new-pose simulation keeps actual breathing poses above 70 percent while completing scheduled eating and drinking");
        }
    }
}
