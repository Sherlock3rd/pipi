using Chenpi;

public static class CareRequestChecks
{
    public static void Run(Action<bool,string> check)
    {
        void Advance(PetEngine cat,double seconds)
        {for(int i=0;i<(int)Math.Ceiling(seconds/.1);i++)cat.Update(.1,12);}
        PetState State()=>new(){X=600,Y=852,TotalSeconds=1000,Food=0,Water=0,Litter=0,
            FoodClock=new(){NextDue=2000,UnavailableSince=0},WaterClock=new(){NextDue=3000,UnavailableSince=0},LitterClock=new(){NextDue=99999},
            FoodPosition=new(900,852),WaterPosition=new(500,852),LitterPosition=new(1300,852),NestPosition=new(1900,852)};
        foreach(string kind in new[]{"food","water"})
        {
            var state=State();var cat=new PetEngine(state,5);cat.Layout(2200,900);Advance(cat,120);
            check(state.CareRequest is null,"long-empty bowls never request before an actual meal or drink is due: "+kind);
            var clock=kind=="food"?state.FoodClock:state.WaterClock;clock.NextDue=1001;
            cat.AdvanceNeeds(1);Advance(cat,100);
            check(state.CareRequest==kind&&cat.Action=="request-"+kind&&new Spot(state.X,state.Y)==cat.OnGround(cat.ObjectPosition(kind)),"due unmet demand wakes and requests at the correct bowl: "+kind);
            check(state.Food==0&&state.Water==0,"request gestures cannot consume absent stock: "+kind);
            var oldX=state.X;cat.MoveObject(kind,new(1600,cat.GroundY));cat.Update(.1,12);
            check(cat.Action=="request-walk"&&Math.Abs(state.X-oldX)<5,"moving an empty requested bowl starts real travel without teleporting: "+kind);
            Advance(cat,100);
            check(new Spot(state.X,state.Y)==cat.RequestDestination(kind)&&cat.Action=="request-"+kind,"request follows relocated bowl without requiring pointer guidance: "+kind);
            cat.Refill(kind);Advance(cat,100);
            check(state.CareRequest is null&&(kind=="food"?state.Hunger<26:state.Thirst<24)&&(kind=="food"?state.Food<20:state.Water<20),"refilling ends request and overdue demand is satisfied by actual consumption: "+kind);
        }
        var stale=State();stale.CareRequest="food";stale.Guiding=true;
        var migrated=new PetEngine(stale,1);migrated.Layout(2200,900);Advance(migrated,5);
        check(stale.CareRequest is null&&!stale.Guiding&&stale.FoodClock.NextDue==2000&&stale.FoodClock.UnavailableSince==0,"legacy premature request is cancelled without altering clocks or stock");
        var full=State();full.Litter=100;full.LitterClock.UnavailableSince=0;full.LitterPosition=new(1000,852);full.FoodPosition=new(750,852);full.WaterPosition=new(350,852);
        var tray=new PetEngine(full,1);tray.Layout(2200,900);Advance(tray,100);
        check(full.CareRequest=="litter"&&tray.Action=="request-litter"&&tray.IsClearRestSpot(full.X)&&full.X>tray.LitterSpot.X,"blocked left of full tray chooses a clear right-side request spot despite no toilet demand");
        var firstGoal=tray.RequestSpot;tray.MoveObject("litter",new(200,tray.GroundY));Advance(tray,100);
        check(tray.Action=="request-litter"&&tray.IsClearRestSpot(full.X)&&new Spot(full.X,full.Y)==tray.RequestSpot&&tray.RequestSpot!=firstGoal,"relocated tray near screen edge recomputes a legal nearby empty spot");
        tray.MoveObject("food",new(full.X,tray.GroundY));Advance(tray,100);
        check(tray.Action=="request-litter"&&tray.IsClearRestSpot(full.X),"furniture occupying a cleaning request spot triggers a fresh empty-space search");
        tray.Refill("litter");Advance(tray,2);
        check(full.CareRequest is null&&full.Litter==80,"cleaning cancels the request without consuming food or water");
    }
}
