using Chenpi;

public static class MovementChecks
{
    public static void Run(Action<bool,string> check)
    {
        BehaviorSettings Settings(string? only=null)
        {
            var s=new BehaviorSettings();s.Values["move.delay.min"]=s.Values["move.delay.max"]=1;s.Values["move.chance"]=100;
            if(only is not null)foreach(string id in new[]{"left","center","right","wall"})s.Values["move."+id]=id==only?1:0;
            return s;
        }
        PetEngine Cat(int seed,BehaviorSettings? s=null)
        {
            var e=new PetEngine(new PetState{X=400,Y=752,Energy=100,RestDuration=3600,Food=100,Water=100,
                NestPosition=new(3100,752),FoodPosition=new(1600,752),WaterPosition=new(1850,752),LitterPosition=new(2700,752),
                FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}},seed,s??Settings());
            e.Layout(4000,800);e.ExpressionsEnabled=true;return e;
        }
        void Tick(PetEngine e,double seconds){for(int i=0;i<seconds*20;i++)e.Update(.05,12);}
        var counts=new Dictionary<string,int>();
        for(int seed=0;seed<800;seed++)
        {
            var e=Cat(seed);Tick(e,1.2);string id=e.LastMovementChoice.StartsWith("wall-")?"wall":e.LastMovementChoice;
            counts[id]=counts.GetValueOrDefault(id)+1;
        }
        check(counts.Count==4&&counts.Values.All(n=>n>140&&n<260),"ordinary destinations and combined wall destination share equal weighted probability");
        Console.WriteLine("Movement selections: "+string.Join(", ",counts.Select(p=>$"{p.Key}={p.Value}")));
        foreach(string id in new[]{"left","center","right","wall"})
        {
            var e=Cat(3,Settings(id));Tick(e,1.2);
            check(e.Action=="walk"&&(id=="wall"?e.LastMovementChoice.StartsWith("wall-"):e.LastMovementChoice=="move-"+id),"single positive destination weight selects "+id+" from distant idle position");
        }
        {
            var e=Cat(2,Settings("wall"));e.MoveObject("nest",new(170,752));
            var choices=e.MovementChoices();check(choices.Count==1&&choices[0].Id=="wall-right"&&choices[0].Weight==1,"blocked left wall removed; right keeps full wall weight");
            Tick(e,20);check(e.LastMovementChoice=="wall-right","autonomous choice ignores blocked nearer edge");
            e.MoveObject("nest",new(3830,752));check(e.Action=="walk"&&e.Reason.Contains("屏边被占用"),"moving furniture into wall destination reroutes current trip safely");
        }
        {
            var s=Settings("none");s.Validate();var e=Cat(2,s);Tick(e,40);
            check(e.LastMovementChoice==""&&e.State.Sleeping,"all zero destination weights allow ordinary sleep without random exception");
            e=Cat(2,Settings("wall"));s=e.Settings.Copy();s.Values["wall.enabled"]=0;e.ApplyBehaviorSettings(s);Tick(e,40);
            check(e.LastMovementChoice==""&&e.State.Sleeping,"wall disabled is excluded from autonomous movement");
        }
        {
            var e=Cat(3);e.PreviewRest("M");Tick(e,120);check(e.LastMovementChoice==""&&e.State.Sleeping,"movement timer never wakes sleeping cat");
            e=Cat(3);e.State.FoodClock.NextDue=0;Tick(e,1.2);check(e.LastMovementChoice==""&&e.ActiveBehaviorNode=="movement"&&e.Reason=="随机照料计时到期","overdue care wins before optional autonomous movement");
            e=Cat(3);Tick(e,1.2);string first=e.LastMovementChoice;Tick(e,1);check(e.LastMovementChoice==first,"in-progress trip is not redrawn each frame");
            e=Cat(3,Settings("wall"));Tick(e,1.2);e.State.FoodClock.NextDue=0;Tick(e,.1);check(e.Reason=="随机照料计时到期","care can preempt optional trip toward a wall");
        }
        {
            var s=Settings();s.Values["wall.chance"]=0;s.Values["sleep.delay"]=19;
            var parsed=BehaviorSettingsStore.Parse(System.Text.Json.JsonSerializer.Serialize(s));
            check(parsed.Get("sleep.delay")==19&&parsed.Get("move.wall")==1,"old wall probability does not discard saved user settings or skew destination weights");
        }
    }
}
