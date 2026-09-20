using System;

namespace Chenpi;

public sealed class CareClock
{
    public double NextDue {get;set;}
    public double? UnavailableSince {get;set;}
    public double LastRequested {get;set;}=-1e12;
}

public sealed partial class PetEngine
{
    private static readonly string[] CareKinds={"water","food","litter"};
    public const double RequestDelay=300;
    public const double FollowRadius=190;
    private Spot guidePointer;
    private bool pointerKnown;
    private double guideTravelled;
    private bool guideFollowing;
    public Spot RequestSpot=>State.CareRequest is string kind?RequestDestination(kind):NearestRestSpot(new(State.X,State.Y));
    private CareClock ClockFor(string kind)=>kind switch {"food"=>State.FoodClock,"water"=>State.WaterClock,_=>State.LitterClock};
    private bool Available(string kind)=>kind switch {"food"=>State.Food>0,"water"=>State.Water>0,_=>State.Litter<100};
    // Eating/drinking demand uses the same persisted schedule as actual visits.
    // A full tray still asks for cleaning independently of the next toilet visit.
    private bool HasCareDemand(string kind)=>kind=="litter"||ClockFor(kind).NextDue<=State.TotalSeconds;
    private void InitializeCare()
    {
        foreach(var kind in CareKinds)if(ClockFor(kind).NextDue<=0)ScheduleNext(kind);
        if(State.CareRequest is not null&&Array.IndexOf(CareKinds,State.CareRequest)<0){State.CareRequest=null;State.Guiding=false;}
        RefreshAvailability();
        if(State.CareRequest is string pending&&!HasCareDemand(pending))CancelCareRequest();
    }
    private void ScheduleNext(string kind)
    {
        ClockFor(kind).NextDue=State.TotalSeconds+Settings.Range(random,kind,60);Dirty=true;
    }
    private void RefreshAvailability()
    {
        foreach(var kind in CareKinds)
        {
            var clock=ClockFor(kind);
            if(Available(kind))clock.UnavailableSince=null;
            else clock.UnavailableSince??=State.TotalSeconds;
        }
    }
    private string? DueCare()
    {
        if(AutomaticCareBlocked)return null;
        string? chosen=null;double first=double.MaxValue;
        foreach(var kind in CareKinds)
        {
            var clock=ClockFor(kind);
            if(Available(kind)&&clock.NextDue<=State.TotalSeconds&&clock.NextDue<first){chosen=kind;first=clock.NextDue;}
        }
        return chosen;
    }
    private string? ReadyRequest(string? except=null)
    {
        if(AutomaticCareBlocked)return null;
        RefreshAvailability();
        string? chosen=null;double oldest=double.MaxValue;
        foreach(var kind in CareKinds)
        {
            var clock=ClockFor(kind);
            if(kind!=except&&HasCareDemand(kind)&&!Available(kind)&&clock.UnavailableSince is double start&&State.TotalSeconds-start>=Settings.Get("request.delay")&&clock.LastRequested<oldest)
            {chosen=kind;oldest=clock.LastRequested;}
        }
        return chosen;
    }
    private bool CanStartCare()=>Action is "idle" or "sit" or "sleep" || Action.StartsWith("rest-")||Action=="walk"&&arrival is "settle" or "sleep";
    private void StartCare(string kind)=>Go(ObjectPosition(kind),kind switch {"food"=>"eat","water"=>"drink",_=>"toilet"},"随机照料计时到期");
    private void BeginRequest(string kind)
    {
        State.CareRequest=kind;State.Guiding=false;ClockFor(kind).LastRequested=State.TotalSeconds;
        SetAction("request-walk",double.MaxValue,"前往对应物品请求照料");
    }
    private void CancelCareRequest()
    {State.CareRequest=null;State.Guiding=false;guideTravelled=0;pointerKnown=false;Dirty=true;}
    private void BeginGuide()
    {
        if(State.CareRequest is null)return;
        State.Guiding=true;guideTravelled=0;guideFollowing=false;LastInteraction=Now;
        if(!pointerKnown){guidePointer=CatPlayCenter;pointerKnown=true;}
        SetAction("guide-walk",double.MaxValue,"引导前往照料物品");
    }
    private void FinishRequest()
    {
        bool guided=State.Guiding;State.CareRequest=null;State.Guiding=false;guideTravelled=0;NewRest();
        SetAction(guided?"care-thanks":"sit",guided?2:1,"照料已完成");
    }
    public Spot RequestDestination(string kind)=>kind switch {
        "food"=>CareDestination(FoodSpot,"eat"),
        "water"=>CareDestination(WaterSpot,"drink"),
        _=>NearestRestSpot(OnGround(LitterSpot))};
    public Spot GuideDestination(string kind)=>RequestDestination(kind);
    private bool UpdateCareFlow(double dt)
    {
        string? kind=State.CareRequest;if(kind is null)return false;
        if(!HasCareDemand(kind)){CancelCareRequest();NewRest();SetAction("sit",1,"当前没有吃喝需求");return true;}
        if(Available(kind)){FinishRequest();return true;}
        if(!State.Guiding)
        {
            var requestGoal=RequestDestination(kind);
            double distance=new Spot(State.X,State.Y).Distance(requestGoal);
            if(distance>2||!MovementComplete)
            {
                if(Action!="request-walk")SetAction("request-walk",double.MaxValue,"前往请求位置");
                FacingLeft=requestGoal.X<State.X;MoveTowards(requestGoal,WalkSpeed*dt);return true;
            }
            State.X=requestGoal.X;State.Y=requestGoal.Y;
            if(Action!="request-"+kind)
            {
                if(ExpressionsEnabled&&VisualPoseReady?.Invoke("F",Now)==false)return true;
                SetAction("request-"+kind,double.MaxValue,"等待主人注意");
                if(Now-LastBeg>=Settings.Get("request.sound")){LastBeg=Now;RequestedAttention?.Invoke();}
            }
            if(ActionTime>=Settings.Get("request.rotate")&&ReadyRequest(kind) is string next)BeginRequest(next);
            return true;
        }
        var goal=GuideDestination(kind);
        double remaining=new Spot(State.X,State.Y).Distance(goal);
        if(remaining<=2&&MovementComplete)
        {
            State.X=goal.X;State.Y=goal.Y;
            if(Action!="guide-"+kind)
            {
                // The supplied item-gesture clips start in right standing pose.
                FacingLeft=false;
                if(Action!="guide-arrive")SetAction("guide-arrive",double.MaxValue,"停步后示意照料");
                if(VisualStandReady?.Invoke(Now,false)==false)return true;
                SetAction("guide-"+kind,double.MaxValue,"在物品旁示意照料");
            }
            return true;
        }
        guideFollowing=pointerKnown&&CatPlayCenter.Distance(guidePointer)<=(Settings.Get("guide.radius")+(guideFollowing?40:0));
        double lookDuration=VisualActionDuration?.Invoke("guide-look",FacingLeft)??.6;
        if(Action=="guide-look")
        {
            if(ActionTime<lookDuration||!guideFollowing||ActionTime%lookDuration>dt+.000001)return true;
            guideTravelled=0;
        }
        else if(Action=="guide-stop"||(IsClearRestSpot(State.X)&&(!guideFollowing||guideTravelled>=Settings.Get("guide.step"))))
        {
            if(Action!="guide-stop")SetAction("guide-stop",double.MaxValue,"停步后回头等待");
            if(VisualStandReady?.Invoke(Now,FacingLeft)!=false)SetAction("guide-look",lookDuration,"回头等主人跟上");
            return true;
        }
        if(Action!="guide-walk")SetAction("guide-walk",double.MaxValue,"带路到物品旁");
        FacingLeft=goal.X<State.X;double step=Math.Min(remaining,WalkSpeed*dt);
        double previousX=State.X,previousY=State.Y;MoveTowards(goal,step);
        guideTravelled+=new Spot(previousX,previousY).Distance(new Spot(State.X,State.Y));
        return true;
    }
}
