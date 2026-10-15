using Chenpi;
using System.Text.Json;

public static class BehaviorUpdateChecks
{
    public static void Run(Action<bool,string> check,string manifestPath)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(manifestPath));
        var animations=doc.RootElement.GetProperty("animations");
        (PetEngine Cat,SpritePlayback Player) Setup(double x,bool due)
        {
            var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
            var s=new PetState{X=x,Y=752,TotalSeconds=1000,Food=100,Water=100,Litter=0,RestDuration=3600,
                FoodPosition=new(800,752),WaterPosition=new(1000,752),NestPosition=new(1700,752),LitterPosition=new(1450,752),
                FoodClock=new(){NextDue=due?1:99999},WaterClock=new(){NextDue=due?2:99999},LitterClock=new(){NextDue=99999}};
            var c=new PetEngine(s,7){ExpressionsEnabled=true,CompletionEnabled=p.HasCompletion,VisualPoseReady=p.PreparePose,
                VisualStandReady=p.PrepareStand,VisualCareReady=p.PrepareCare,VisualTravel=p.TravelTo,VisualActionDuration=p.ActionDuration,
                VisualConsumptionWindow=p.ConsumptionWindow,VisualVelocity=p.HorizontalVelocity};
            c.Layout(2200,800);return(c,p);
        }
        SpriteFrame Tick(PetEngine c,SpritePlayback p)
        {c.AdvanceNeeds(1d/60);c.Update(1d/60,12);return p.Sample(c.AligningForCare?"care-ready":c.Action,c.Now,c.FacingLeft)!.Value;}
        foreach(double x in new[]{300d,600d,1000d})
        {
            var (c,p)=Setup(x,true);bool ate=false,detour=false,unnecessaryTurn=false;
            for(int i=0;i<100*60&&c.State.Water>80;i++)
            {
                var f=Tick(c,p);ate|=c.Action=="eat";
                detour|=c.Reason=="到物品旁边的空位休息";
                if(ate&&c.Action=="walk")unnecessaryTurn|=f.Clip is "video-15" or "video-16";
            }
            check(c.State.Food==80&&c.State.Water==80&&!detour&&!unnecessaryTurn,
                $"due meals at {x} commit directly to consumption and next bowl without a rest detour or double turn");
        }
        foreach(bool left in new[]{false,true})
        {
            var (c,p)=Setup(350,false);p.RestorePose(left?"SL":"SR");
            c.SetToy(true,new(c.State.X+(left?-40:40),c.CatPlayCenter.Y));
            var caught=new List<string>();int maxFrame=0;bool moved=false;double beganX=c.State.X;
            for(int i=0;i<18*60;i++)
            {
                var f=Tick(c,p);if(caught.Count==0||caught[^1]!=f.Clip)caught.Add(f.Clip);
                if(f.Clip==(left?"video-36":"video-35"))maxFrame=Math.Max(maxFrame,f.Index);
                moved|=Math.Abs(c.State.X-beganX)>.001;
            }
            check(maxFrame>100&&!moved&&c.Action=="toy-bat"&&caught.Count<=3,
                $"{left}: nearby toy completes and repeats catch cycles without resetting to standing every tick");
            c.SetToy(true,new(left?170:560,c.CatPlayCenter.Y));
            bool ran=false;double before=c.State.X;
            for(int i=0;i<20*60;i++)
            {
                var f=Tick(c,p);ran|=f.Clip==(left?"video-112":"video-109");
                // Keep the target outside the catch circle while the gait reaches a loop.
                c.SetToy(true,new(c.State.X+(left?-200:200),c.CatPlayCenter.Y));
                if(ran)break;
            }
            check(ran&&(left?c.State.X<before:c.State.X>before),$"{left}: moved toy exits catch and uses independent authored running gait");
            c.SetToy(false,c.ToyTip);Tick(c,p);
            check(!c.Action.StartsWith("toy-"),$"{left}: returning wand cancels pursuit");
        }
        foreach(string kind in new[]{"food","water","litter"})foreach(bool guided in new[]{false,true})
        {
            var (c,p)=Setup(350,false);var s=c.State;
            if(kind=="food")s.Food=0;else if(kind=="water")s.Water=0;else s.Litter=100;
            var clock=kind=="food"?s.FoodClock:kind=="water"?s.WaterClock:s.LitterClock;
            clock.NextDue=1;clock.UnavailableSince=0;s.CareRequest=kind;s.Guiding=guided;
            s.X=(guided?c.GuideDestination(kind):c.RequestDestination(kind)).X;
            p.RestorePose(guided?"SR":"F");
            int calls=0,lastCallFrame=-1;string previous="";bool moved=false;
            for(int i=0;i<60*90;i++)
            {
                var f=Tick(c,p);if(f.Clip=="video-88"&&previous!=f.Clip)calls++;
                if(f.Clip=="video-88")lastCallFrame=f.Index;
                previous=f.Clip;
                if(!moved&&c.Now>40){c.MoveObject(kind,new(c.ObjectPosition(kind).X+80,c.GroundY));moved=true;}
            }
            check(calls==1&&lastCallFrame==p.InspectFrame("video-88",0)!.Value.Definition.Count-1&&clock.RequestCallPlayed,
                $"{kind}/{guided}: request inserts one complete call and relocating its object does not replay it");
            var saved=JsonSerializer.Deserialize<PetState>(JsonSerializer.Serialize(s))!;
            var savedClock=kind=="food"?saved.FoodClock:kind=="water"?saved.WaterClock:saved.LitterClock;
            check(savedClock.RequestCallPlayed,$"{kind}/{guided}: request-call marker survives serialization");
            var (_,reloadedPlayer)=Setup(350,false);
            var reloaded=new PetEngine(saved,7){ExpressionsEnabled=true,CompletionEnabled=true,VisualPoseReady=reloadedPlayer.PreparePose,
                VisualStandReady=reloadedPlayer.PrepareStand,VisualCareReady=reloadedPlayer.PrepareCare,VisualTravel=reloadedPlayer.TravelTo,
                VisualActionDuration=reloadedPlayer.ActionDuration,VisualConsumptionWindow=reloadedPlayer.ConsumptionWindow};
            reloaded.Layout(2200,800);bool repeated=false;
            for(int i=0;i<45*60;i++)repeated|=Tick(reloaded,reloadedPlayer).Clip=="video-88";
            check(!repeated,$"{kind}/{guided}: reloading the same unmet demand does not call again");
            c.Refill(kind);check(!clock.RequestCallPlayed,$"{kind}/{guided}: satisfying requested supply resets its call marker");
        }
        foreach(bool guided in new[]{false,true})
        {
            var (c,p)=Setup(350,false);var s=c.State;s.Food=0;s.FoodClock.NextDue=1;s.FoodClock.UnavailableSince=0;s.CareRequest="food";s.Guiding=guided;
            c.VisualRequestFinishReady=p.PrepareRequestFinish;
            s.X=(guided?c.GuideDestination("food"):c.RequestDestination("food")).X;p.RestorePose(guided?"SR":"F");
            SpriteFrame f=default;for(int i=0;i<30*60;i++){f=Tick(c,p);if(f.Clip=="video-88"&&f.Index>=30)break;}
            check(f.Clip=="video-88",$"{guided}: fixture reaches the call before refill");
            c.Refill("food");int last=-1;double foodAtRefill=s.Food;bool thanks=false;
            for(int i=0;i<30*60;i++)
            {
                f=Tick(c,p);if(f.Clip=="video-88")last=f.Index;
                thanks|=f.Clip=="video-119";
                if(c.Action!="care-finish")break;
            }
            check(last==p.InspectFrame("video-88",0)!.Value.Definition.Count-1&&s.Food==foodAtRefill&&(!guided||thanks),
                $"{guided}: refill during the call finishes one mouth cycle then enters the correct thanks pose without consuming stock");
        }
    }
}
