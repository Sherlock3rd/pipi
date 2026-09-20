using Chenpi;
using System.Text.Json;

public static class RequestGestureChecks
{
    public static void Run(Action<bool,string> check,string manifestPath)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(manifestPath));var animations=doc.RootElement.GetProperty("animations");
        SpritePlayback Player(){var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);return p;}
        foreach(string kind in new[]{"food","water"})foreach(bool guided in new[]{false,true})foreach(double phase in new[]{.4,2.1,4.9,5.6,9.9})
        {
            var p=Player();var state=new PetState{X=800,Y=752,TotalSeconds=1000,Food=0,Water=0,Litter=0,RestDuration=3600,
                CareRequest=kind,Guiding=guided,NestPosition=new(2000,752),FoodPosition=new(800,752),WaterPosition=new(1300,752),LitterPosition=new(1750,752),
                FoodClock=new(){NextDue=kind=="food"?1:99999,UnavailableSince=0},WaterClock=new(){NextDue=kind=="water"?1:99999,UnavailableSince=0},LitterClock=new(){NextDue=99999}};
            var c=new PetEngine(state,7){ExpressionsEnabled=true,CompletionEnabled=p.HasCompletion,VisualPoseReady=p.PreparePose,VisualStandReady=p.PrepareStand,VisualCareReady=p.PrepareCare,
                VisualRequestFinishReady=p.PrepareRequestFinish,VisualTravel=p.TravelTo,VisualActionDuration=p.ActionDuration,VisualConsumptionWindow=p.ConsumptionWindow};
            c.Layout(2400,800);state.X=(guided?c.GuideDestination(kind):c.RequestDestination(kind)).X;p.RestorePose(guided?"SR":"F");
            SpriteFrame Tick(){c.Update(1d/60,12);return p.Sample(c.AligningForCare?"care-ready":c.Action,c.Now,c.FacingLeft)!.Value;}
            string gesture="video-"+(guided?(kind=="food"?"48":"49"):(kind=="food"?"45":"46"));
            for(int i=0;i<600&&c.Action!=(guided?"guide-":"request-")+kind;i++)Tick();
            for(int i=0;i<(int)(phase*60);i++)Tick();
            var before=p.Sample(c.Action,c.Now,c.FacingLeft)!.Value;double x=state.X,deadline=(kind=="food"?state.FoodClock:state.WaterClock).NextDue;
            c.Refill(kind);var immediate=p.Sample(c.Action,c.Now,c.FacingLeft)!.Value;
            check(before.Clip==gesture&&immediate.Clip==before.Clip&&immediate.Index==before.Index&&(kind=="food"?state.Food:state.Water)==20&&state.CareRequest is null,
                $"{kind}/{guided}/{phase}: refill updates stock immediately without replacing the displayed gesture frame");
            var order=new List<string>();int lastGesture=-1;bool stationary=true;
            for(int i=0;i<1800;i++)
            {
                string action=c.Action;var f=Tick();if(order.Count==0||order[^1]!=f.Clip)order.Add(f.Clip);
                if(f.Clip==gesture)lastGesture=f.Index;
                if(action=="care-finish")stationary&=Math.Abs(state.X-x)<.00001;
                if(c.Action!="care-finish")break;
            }
            check(lastGesture==before.Definition.Count-1&&stationary&&(!guided||!order.Contains("video-07")&&order[^1]=="video-119"),
                $"{kind}/{guided}/{phase}: current loop reaches its endpoint; standing gesture thanks standing with no root jump");
            check((kind=="food"?state.Food:state.Water)==20&&(kind=="food"?state.FoodClock:state.WaterClock).NextDue==deadline,
                $"{kind}/{guided}/{phase}: finishing and thanks do not consume or reschedule care");
            if(phase==2.1)
            {
                for(int i=0;i<12000&&(kind=="food"?state.Food:state.Water)>0;i++)Tick();
                check((kind=="food"?state.Food:state.Water)==0,"completed request resumes actual overdue consumption: "+kind+"/"+guided);
            }
        }
        {
            var p=Player();p.Sample("guide-water",0);p.Sample("guide-water",2);p.PrepareRequestFinish(2);
            check(p.Sample("drag",2.01)!.Value.Clip=="video-32","explicit pickup still interrupts a pending gesture finish immediately");
        }
        {
            var p=Player();p.Sample("guide-water",0);double now=2;var last=new Dictionary<string,int>();
            for(int i=0;i<400;i++)
            {
                bool ready=p.PrepareRequestFinish(now);var frame=p.Sample("care-finish",now)!.Value;last[frame.Clip]=frame.Index;
                if(ready)break;now+=1d/24;
            }
            check(last["video-49"]==120&&!last.ContainsKey("video-07"),
                "24 Hz refill playback displays the last authored gesture then remains standing");
        }
    }
}
