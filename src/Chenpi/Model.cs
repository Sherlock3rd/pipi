using System;
using System.Collections.Generic;

namespace Chenpi;

public sealed class PetState
{
    public int Version { get; set; } = 1;
    public DateTimeOffset AdoptedAt { get; set; } = DateTimeOffset.UtcNow;
    public double Hunger { get; set; } = 26;
    public double Thirst { get; set; } = 24;
    public double Food { get; set; } = 70;
    public double Water { get; set; } = 75;
    public double Litter { get; set; } = 15;
    public double Bladder { get; set; } = 10;
    public double Energy { get; set; } = 85;
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
    public bool Sleeping { get; set; }
    public bool SleepingInNest { get; set; } = true;
    public double RestDuration { get; set; }
    public double RestElapsed { get; set; }
    public double StillSeconds { get; set; }
    public bool Floating { get; set; }
    public bool Muted { get; set; } = true;
    public double Volume { get; set; } = 0.25;
    public double Scale { get; set; } = 0.85;
    public Spot? NestPosition { get; set; }
    public Spot? FoodPosition { get; set; }
    public Spot? WaterPosition { get; set; }
    public Spot? LitterPosition { get; set; }
    public double LayoutWidth { get; set; }
    public double LayoutHeight { get; set; }
    public string BootId { get; set; } = "";
    public double AwakeSeconds { get; set; }
    public double TotalSeconds { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool ClockGap { get; set; }
    public CareClock FoodClock {get;set;}=new();
    public CareClock WaterClock {get;set;}=new();
    public CareClock LitterClock {get;set;}=new();
    public string? CareRequest {get;set;}
    public bool Guiding {get;set;}
}

public readonly record struct Spot(double X, double Y)
{
    public double Distance(Spot other) => Math.Sqrt(Math.Pow(X-other.X,2)+Math.Pow(Y-other.Y,2));
}

// Selector and sequence compose decisions; the animation runner owns action duration.
public interface IBehaviorNode { bool Tick(); }
public sealed class Condition(Func<bool> predicate) : IBehaviorNode { public bool Tick() => predicate(); }
public sealed class BehaviorAction(Action action) : IBehaviorNode { public bool Tick() { action(); return true; } }
public sealed class Sequence(params IBehaviorNode[] children) : IBehaviorNode
{
    public bool Tick() { foreach (var child in children) if (!child.Tick()) return false; return true; }
}
public sealed class Selector(params IBehaviorNode[] children) : IBehaviorNode
{
    public bool Tick() { foreach (var child in children) if (child.Tick()) return true; return false; }
}

public sealed partial class PetEngine
{
    public PetState State { get; }
    public string Action { get; private set; } = "idle";
    public string Reason { get; private set; } = "适应新家";
    public double ActionTime { get; private set; }
    public long ActionRevision { get; private set; }
    public double Now { get; private set; }
    public double LastInteraction { get; private set; }
    public double LastBeg { get; private set; } = -300;
    public bool FacingLeft { get; private set; }
    // Only position waits for the visual stand/turn sequence; clocks and input keep running.
    public Func<string,double,bool,bool>? CanAdvanceMovement {get;set;}
    public Func<string,double,bool,double?>? VisualVelocity {get;set;}
    public Func<string,bool,double?>? VisualActionDuration {get;set;}
    public Func<double,bool>? VisualCareReady {get;set;}
    public Func<double,bool,bool>? VisualStandReady {get;set;}
    public bool AligningForCare {get;private set;}
    public Func<string,(double Start,double Duration)?>? VisualConsumptionWindow {get;set;}
    public bool Holding { get; set; }
    public bool Dirty { get; set; }
    public Spot Nest { get; set; }
    public Spot FoodSpot { get; set; }
    public Spot WaterSpot { get; set; }
    public Spot LitterSpot { get; set; }
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 800;
    public event Action? RequestedAttention;
    private readonly Random random;
    private readonly Selector tree;
    private readonly Selector toyTree;
    private double toyStep;
    private Spot target;
    private string arrival = "idle";
    private double duration = 3;
    private int bites;
    private double sleepGrace;
    public const double NestSleepGrace=15;
    private bool manualSequence;
    private double careResumeAfter;
    private bool AutomaticCareBlocked=>manualSequence||Now<careResumeAfter||(Action=="sleep"&&Now<sleepGrace);
    private double nightRestUntil;
    private int localHour;
    public const double QuietSleepDelay=60;
    private int clickStreak;
    private double hoverSeconds;
    private bool hoverTriggered;
    public bool ToyHeld {get;private set;}
    public Spot ToyTip {get;private set;}
    public const double CatPlayRadius=390;
    public const double ToyPlayRadius=170;
    public const double CatCatchRadius=100;
    public const double WalkSpeed=36;
    public const double ToyRunSpeed=WalkSpeed;
    public Spot CatPlayCenter=>new(State.X,State.Y-65);
    public bool ToyOverlaps=>ToyHeld&&CatPlayCenter.Distance(ToyTip)<=CatPlayRadius+ToyPlayRadius;
    // The outer rings attract attention. Catching requires the actual feather tip near the cat.
    public bool ToyWithinCatchRange=>ToyHeld&&CatPlayCenter.Distance(ToyTip)<=CatCatchRadius;
    public bool Grounded=>State.LayoutWidth>0;
    public double GroundY=>Height-48;
    public SurfaceSupport Support {get;}=new();
    public string? ApproachingSupport=>Action=="walk"?arrival switch {"sleep"=>"nest","toilet" or "bury"=>"litter",_=>null}:null;
    public Spot OnGround(Spot p)=>Grounded?new(p.X,GroundY):p;
    public void DragTo(Spot pointerPosition)
    {State.X=Math.Clamp(pointerPosition.X,65,Width-65);State.Y=Grounded?GroundY:Math.Clamp(pointerPosition.Y,140,Height-60);Dirty=true;}
    public Spot WandHome=>new(Math.Clamp(Nest.X+140,60,Width-40),Grounded?GroundY-35:Math.Max(55,Nest.Y-167));
    public bool CanDropInNest(Spot pointer)=>new Spot(State.X,State.Y).Distance(Nest)<82
        ||(pointer.X>=Nest.X-InteractionGeometry.NestHalfWidth&&pointer.X<=Nest.X+InteractionGeometry.NestHalfWidth&&pointer.Y>=Nest.Y-InteractionGeometry.NestHeight&&pointer.Y<=Nest.Y);

    public void Layout(double width,double height,bool reset=false)
    {
        if(!reset&&State.LayoutWidth>0&&State.LayoutHeight>0&&(State.LayoutWidth!=width||State.LayoutHeight!=height))
        {
            double sx=width/State.LayoutWidth,sy=height/State.LayoutHeight;
            Spot? Resize(Spot? p)=>p is Spot v?new Spot(v.X*sx,v.Y*sy):null;
            State.NestPosition=Resize(State.NestPosition);State.FoodPosition=Resize(State.FoodPosition);State.WaterPosition=Resize(State.WaterPosition);State.LitterPosition=Resize(State.LitterPosition);
            if(State.X>=0){State.X*=sx;State.Y*=sy;}
        }
        State.LayoutWidth=width;State.LayoutHeight=height;
        Width=width;Height=height;
        Spot Clamp(Spot p,double side,double top,double bottom)=>new(Math.Clamp(p.X,side,width-side),GroundY);
        double homeX=width-InteractionGeometry.NestHalfWidth-24;
        Nest=Clamp(!reset&&State.NestPosition is Spot n?n:new(homeX,GroundY),InteractionGeometry.NestHalfWidth+6,InteractionGeometry.NestHeight,55);
        FoodSpot=Clamp(!reset&&State.FoodPosition is Spot f?f:new(homeX-InteractionGeometry.NestHalfWidth-48,GroundY),48,50,40);
        WaterSpot=Clamp(!reset&&State.WaterPosition is Spot w?w:new(homeX-InteractionGeometry.NestHalfWidth-140,GroundY),48,50,40);
        FoodSpot=new Spot(Math.Max(65+InteractionGeometry.MouthOffsetX,FoodSpot.X),GroundY);
        WaterSpot=new Spot(Math.Max(65+InteractionGeometry.MouthOffsetX,WaterSpot.X),GroundY);
        LitterSpot=Clamp(!reset&&State.LitterPosition is Spot l?l:new(homeX-InteractionGeometry.NestHalfWidth-332,GroundY),InteractionGeometry.LitterHalfWidth,50,40);
        LitterSpot=new Spot(Math.Max(InteractionGeometry.LitterMinimumX,LitterSpot.X),GroundY);
        RememberLayout();
        if(State.X<0||reset){State.X=Nest.X-115;State.Y=Nest.Y-70;}
        State.X=Math.Clamp(State.X,65,width-65);State.Y=GroundY;
        if(State.Sleeping&&State.SleepingInNest){State.X=Nest.X;State.Y=Nest.Y;}
        Retarget();
    }
    private void RememberLayout(){State.NestPosition=Nest;State.FoodPosition=FoodSpot;State.WaterPosition=WaterSpot;State.LitterPosition=LitterSpot;Dirty=true;}
    public Spot ObjectPosition(string kind)=>kind switch {"nest"=>Nest,"food"=>FoodSpot,"water"=>WaterSpot,_=>LitterSpot};
    public void MoveObject(string kind,Spot position)
    {
        double side=kind=="nest"?InteractionGeometry.NestHalfWidth+6:kind=="litter"?InteractionGeometry.LitterHalfWidth:48;
        position=OnGround(new Spot(Math.Clamp(position.X,side,Width-side),Math.Clamp(position.Y,kind=="nest"?145:50,Height-(kind=="nest"?55:40))));
        if(Grounded&&kind is "food" or "water")position=new Spot(Math.Max(65+InteractionGeometry.MouthOffsetX,position.X),position.Y);
        if(Grounded&&kind=="litter")position=new Spot(Math.Max(InteractionGeometry.LitterMinimumX,position.X),position.Y);
        switch(kind){case "nest":Nest=position;break;case "food":FoodSpot=position;break;case "water":WaterSpot=position;break;case "litter":LitterSpot=position;break;}
        RememberLayout();
        LastInteraction=Now;
        if(State.Sleeping&&State.SleepingInNest&&kind=="nest"){State.X=Nest.X;State.Y=Nest.Y;}
        Retarget();
        if((kind=="food"&&Action=="eat")||(kind=="water"&&Action=="drink")||(kind=="litter"&&Action is "toilet" or "bury"))
            Go(ObjectPosition(kind),kind switch {"food"=>"eat","water"=>"drink",_=>Action=="bury"?"bury":"toilet"},"物件搬家了，重新找位置");
    }
    private void Retarget()
    {
        if(Action=="walk")target=arrival switch {"eat"=>FoodSpot,"drink"=>WaterSpot,"sleep"=>Nest,"toilet" or "bury"=>LitterSpot,_=>new Spot(Math.Clamp(target.X,65,Width-65),Math.Clamp(target.Y,140,Height-60))};
        target=CareDestination(target,arrival);
        if(Action=="walk"&&arrival=="settle")target=NearestRestSpot(target);
    }

    public PetEngine(PetState state, int? seed = null)
    {
        State = state; random = seed.HasValue?new Random(seed.Value):new Random();
        if(State.RestDuration<120||State.RestDuration>3600)NewRest();
        InitializeCare();
        tree = new Selector(
            Branch(()=>ReadyRequest() is not null, ()=>BeginRequest(ReadyRequest()!)),
            Branch(()=>DueCare() is not null, ()=>StartCare(DueCare()!)),
            Branch(()=>State.StillSeconds>=QuietSleepDelay, SleepHere),
            Branch(()=>State.RestElapsed>=State.RestDuration, SleepHere),
            Branch(()=>State.Energy<24 || ((localHour>=23 || localHour<6) && Now>nightRestUntil && State.Energy<90), SleepHere),
            new BehaviorAction(()=>SetAction("idle",QuietSleepDelay,"安静休息，困了就睡"))
        );
        toyTree=new Selector(Branch(()=>ToyWithinCatchRange,CatchToy),new BehaviorAction(ChaseToy));
        if(state.Sleeping) {bool inNest=state.SleepingInNest;CancelCareRequest();sleepGrace=Now+NestSleepGrace;SetAction("sleep",double.MaxValue,"继续睡觉");state.SleepingInNest=inNest;}
    }
    private void NewRest(){State.RestDuration=random.Next(1200,3601);State.RestElapsed=0;State.StillSeconds=0;Dirty=true;}
    public void ObservePointer(double seconds,bool overCat,Spot cursor)
    {
        guidePointer=cursor;pointerKnown=true;
        if(!Holding&&!ToyHeld&&State.CareRequest is not null)
        {if(overCat&&!State.Guiding&&Action.StartsWith("request-"))BeginGuide();hoverSeconds=0;hoverTriggered=false;return;}
        if(!overCat||Holding||ToyHeld||manualSequence||Action is "drag" or "walk" or "eat" or "drink" or "toilet" or "bury" or "sleep")
        {hoverSeconds=0;hoverTriggered=false;return;}
        if(Action=="rub")return;
        if(hoverTriggered)return;
        hoverSeconds+=Math.Max(0,seconds);
        if(hoverSeconds>=5){hoverTriggered=true;LastInteraction=Now;State.StillSeconds=0;SetAction("rub",4,"悬停蹭鼠标");}
    }
    public void SetToy(bool held,Spot tip)
    {
        ToyHeld=held;ToyTip=tip;
        if(!held&&Action.StartsWith("toy-")){NewRest();SetAction("sit",1,"玩够啦，歇一会儿");}
    }
    private void TickToy(double dt)
    {
        toyStep=dt;LastInteraction=Now;State.StillSeconds=0;
        FacingLeft=ToyTip.X<State.X;
        toyTree.Tick();
    }
    private void ChaseToy()
    {
        if(Action!="toy-run")SetAction("toy-run",double.MaxValue,"向逗猫棒跑动");
        var desired=NearestRestSpot(new Spot(Math.Clamp(ToyTip.X,65,Width-65),Math.Clamp(ToyTip.Y+65,140,Height-60)));
        if(Math.Abs(desired.X-State.X)>.1)FacingLeft=desired.X<State.X;
        MoveTowards(desired,ToyRunSpeed*toyStep);
        if(ToyWithinCatchRange&&IsClearRestSpot(State.X))CatchToy();
    }
    private void CatchToy()
    {
        if(!IsClearRestSpot(State.X)){ChaseToy();return;}
        if(Action!="toy-bat")SetAction("toy-bat",double.MaxValue,"贴近抓逗猫棒");
    }
    private void MoveTowards(Spot destination,double step)
    {
        destination=OnGround(destination);if(Grounded)State.Y=GroundY;
        if(CanAdvanceMovement?.Invoke(Action,Now,FacingLeft)==false)return;
        if(VisualVelocity?.Invoke(Action,Now,FacingLeft) is double velocity)
        {
            double dt=step/(Action=="rub"?24:WalkSpeed);
            step=Math.Abs(velocity)*dt;
            if(Math.Abs(destination.X-State.X)>.1&&velocity*(destination.X-State.X)<0)
            {State.X=Math.Clamp(State.X+velocity*dt,65,Width-65);return;}
        }
        double distance=new Spot(State.X,State.Y).Distance(destination);if(distance<=.01)return;
        double ratio=Math.Min(1,step/distance);State.X+=(destination.X-State.X)*ratio;State.Y+=(destination.Y-State.Y)*ratio;
    }
    private static Sequence Branch(Func<bool> test, Action action) => new(new Condition(test),new BehaviorAction(action));
    public void AdvanceNeeds(double seconds)
    {
        if(!double.IsFinite(seconds) || seconds<0) throw new ArgumentOutOfRangeException(nameof(seconds));
        RefreshAvailability();
        State.Hunger=Math.Clamp(State.Hunger+seconds*12/3600,0,100);
        State.Thirst=Math.Clamp(State.Thirst+seconds*18/3600,0,100);
        State.TotalSeconds+=seconds;
        State.Energy=Math.Clamp(State.Energy+seconds*(State.Sleeping ? 25 : -8)/3600,0,100);
    }
    public void Update(double dt, int hour)
    {
        dt=Math.Clamp(dt,0,0.12); Now+=dt; localHour=hour;if(Grounded)State.Y=GroundY;
        if(Holding) return;
        ActionTime+=dt;
        if(Action=="drag") return;
        if((Action is "idle" or "sit"||Action=="sleep"&&!State.SleepingInNest)&&LeaveOccupiedRestSpot())return;
        if(ToyOverlaps){TickToy(dt);return;}
        if(Action.StartsWith("toy-")){NewRest();SetAction("sit",1,"等主人靠近一点");}
        if(UpdateCareFlow(dt))return;
        if(CanStartCare())
        {
            string? request=ReadyRequest();
            if(request is not null){BeginRequest(request);UpdateCareFlow(dt);return;}
            string? due=DueCare();if(due is not null)StartCare(due);
        }
        // The supplied rub clip already contains the gesture. Do not chase
        // alternating +/-18 mouse offsets underneath a stationary animation.
        if(Action=="walk")
        {
            var here=new Spot(State.X,State.Y); var distance=here.Distance(target);
            if(Math.Abs(target.X-State.X)>.1)FacingLeft=target.X<State.X;
            if(CanAdvanceMovement?.Invoke(Action,Now,FacingLeft)==false)return;
            if(distance<2)
            {
                State.X=target.X;State.Y=target.Y;
                AligningForCare=arrival is "eat" or "drink" or "toilet" or "bury";
                if(AligningForCare&&VisualCareReady?.Invoke(Now)==false)return;
                Arrive();
            }
            else {AligningForCare=false;MoveTowards(target,WalkSpeed*dt);}
            return;
        }
        if(Action is "idle" or "sit"&&VisualVelocity?.Invoke(Action,Now,FacingLeft) is double drift)
            State.X=Math.Clamp(State.X+drift*dt,65,Width-65);
        State.RestElapsed+=dt;State.StillSeconds+=dt;
        if(State.StillSeconds>=QuietSleepDelay&&Action is "idle" or "sit")SleepHere();
        if(Action=="sleep")
        {
            State.Sleeping=true;
            // A rest deadline renews sleep; it is not a reason to pace the desktop.
            if(State.RestElapsed>=State.RestDuration)NewRest();
            return;
        }
        if(Action is "eat" or "drink")
        {
            var consumption=VisualConsumptionWindow?.Invoke(Action);
            int completed=consumption is {} window?(int)(Math.Max(0,ActionTime-window.Start)/Math.Max(.01,window.Duration)*4):(int)(ActionTime/1.1);
            while(bites<completed && bites<4)
            {
                bool food=Action=="eat";
                if(bites==0&&(food?State.Food:State.Water)>0)ScheduleNext(food?"food":"water");
                Consume(food,5);bites++;
            }
            if(consumption is null&&((Action=="eat" && State.Food<=0)||(Action=="drink" && State.Water<=0))) duration=ActionTime;
        }
        if(ActionTime<duration)return;
        if(Action=="land"){NewRest();SetAction("sit",1,"这里也很舒服");return;}
        if(Action=="toilet") {State.Bladder=0; State.Litter=Math.Clamp(State.Litter+20,0,100);ScheduleNext("litter");RefreshAvailability();Dirty=true;Go(LitterSpot,"bury","走到便便旁边埋砂");return;}
        if(Action=="bury") {Go(new Spot(Math.Clamp(LitterSpot.X-80,65,Width-65),Math.Clamp(LitterSpot.Y-65,140,Height-60)),"settle","收拾好啦");return;}
        if(manualSequence){FinishManualSequence();return;}
        if(LeaveOccupiedRestSpot())return;
        tree.Tick();
    }
    public void Consume(bool food, double quantity)
    {
        var taken=Math.Min(Math.Max(quantity,0),food?State.Food:State.Water);
        if(food) {State.Food-=taken;State.Hunger=Math.Max(0,State.Hunger-taken*2);State.Bladder=Math.Min(100,State.Bladder+taken*1.1);}
        else {State.Water-=taken;State.Thirst=Math.Max(0,State.Thirst-taken*2.5);State.Bladder=Math.Min(100,State.Bladder+taken*0.8);}
        RefreshAvailability();
        Dirty=true;
    }
    private void Go(Spot destination,string next,string reason)
    {
        target=next=="settle"?NearestRestSpot(destination):CareDestination(destination,next);if(Math.Abs(target.X-State.X)>.1)FacingLeft=target.X<State.X;
        arrival=next;SetAction("walk",double.MaxValue,reason);
    }
    public Spot LitterTarget=>new(LitterSpot.X+InteractionGeometry.LitterClump(SupplyLayers(State.Litter)-1).X,
        LitterSpot.Y+InteractionGeometry.LitterClump(SupplyLayers(State.Litter)-1).Y);
    public Spot CareDestination(Spot item,string action)
    {
        if(Grounded&&action is "eat" or "drink")item=new(item.X-InteractionGeometry.MouthOffsetX,item.Y);
        if(Grounded&&action=="sleep")item=new(item.X-InteractionGeometry.NestRestOffsetX,item.Y);
        if(Grounded&&action is "toilet" or "bury")
        {
            int slot=SupplyLayers(action=="toilet"?Math.Min(100,State.Litter+20):State.Litter)-1;
            double offset=action=="toilet"?InteractionGeometry.ToiletDepositOffsetX:InteractionGeometry.BuryPawOffsetX;
            item=new(item.X+InteractionGeometry.LitterClump(slot).X-offset,item.Y);
        }
        return OnGround(item);
    }
    private void Arrive()
    {
        NewRest();
        if((arrival=="eat"&&State.Food<=0)||(arrival=="drink"&&State.Water<=0)||(arrival=="toilet"&&State.Litter>=100)||(arrival=="bury"&&State.Litter<=0))
        {if(manualSequence)FinishManualSequence();else SetAction("sit",1,"目标物品不可用");return;}
        if(arrival=="sleep")Sleep(false);
        else if(arrival=="settle")
        {if(manualSequence){FinishManualSequence();return;}SetAction("idle",QuietSleepDelay,"找到舒服的地方，安静歇一会儿");}
        else SetAction(arrival,arrival is "eat" or "drink" ? 4.7 : arrival=="toilet" ? 4 : arrival=="bury" ? 3 : 2,Reason);
    }
    // One shared draw/hit-test anchor. On leaving the nest keep this world point;
    // clearing SleepingInNest must not teleport the displayed cat by (20,10).
    public Spot VisualPosition=>new(State.X-(State.Sleeping&&State.SleepingInNest?InteractionGeometry.NestRestOffsetX:0),State.Y-(!Grounded&&State.Sleeping&&State.SleepingInNest?10:0));
    private void SetAction(string action,double seconds,string reason)
    {
        AligningForCare=false;
        if(action!="sleep"&&State.Sleeping&&State.SleepingInNest)
        {var position=VisualPosition;State.X=position.X;State.Y=position.Y;}
        Action=action;ActionTime=0;ActionRevision++;duration=seconds<double.MaxValue?VisualActionDuration?.Invoke(action,FacingLeft)??seconds:seconds;bites=0;Reason=reason;State.Sleeping=action=="sleep";if(!State.Sleeping)State.SleepingInNest=false;Dirty=true;
    }
    public void Interact()
    {
        if(State.CareRequest is not null){BeginGuide();return;}
        manualSequence=false;
        clickStreak=Now-LastInteraction<6?clickStreak+1:1;LastInteraction=Now;State.StillSeconds=0;
        if(Action=="sleep")Wake();
        else {if(State.RestElapsed>=State.RestDuration)NewRest();SetAction(clickStreak%3==0?"roll":clickStreak%3==2?"paw":"pet",3,"点击互动");}
    }
    private void BeginManualSequence()
    {CancelCareRequest();Holding=false;ToyHeld=false;manualSequence=true;careResumeAfter=0;hoverSeconds=0;hoverTriggered=false;LastInteraction=Now;State.StillSeconds=0;}
    private void FinishManualSequence()
    {manualSequence=false;careResumeAfter=Now+3;NewRest();SetAction("sit",1,"手动动作完成，稍作停留");}
    public void BeginDrag() {BeginManualSequence();manualSequence=false;SetAction("drag",double.MaxValue,"被主人拎起来啦");}
    public void Drop(bool inNest)
    {LastInteraction=Now;if(inNest)Sleep();else SetAction("land",0.7,"稳稳落地");}
    public void Sleep(bool newRest=true)
    {CancelCareRequest();manualSequence=false;Holding=false;ToyHeld=false;State.X=Nest.X;State.Y=Nest.Y;if(newRest)NewRest();sleepGrace=Now+NestSleepGrace;SetAction("sleep",double.MaxValue,"猫窝睡眠");State.SleepingInNest=true;}
    private void SleepHere(){if(LeaveOccupiedRestSpot())return;sleepGrace=Now+NestSleepGrace;SetAction("sleep",double.MaxValue,"就在这里打个盹");State.SleepingInNest=false;}
    public void Wake() {nightRestUntil=Now+300;State.StillSeconds=0;State.RestElapsed=0;SetAction("wake",2.0,"唤醒伸展");}
    public void Refill(string kind)
    {
        LastInteraction=Now;
        if(kind=="food")State.Food=Math.Min(100,(SupplyLayers(State.Food)+1)*20);
        else if(kind=="water")State.Water=Math.Min(100,(SupplyLayers(State.Water)+1)*20);
        else State.Litter=Math.Max(0,(SupplyLayers(State.Litter)-1)*20);
        if(kind=="litter")
        {
            // Cleaning removes the newest clump. Do not scratch an empty target.
            if(Action=="bury"||(Action=="walk"&&arrival=="bury"))Go(new Spot(LitterSpot.X-240,LitterSpot.Y),"settle","已经清理好了");
            else if(Action=="toilet")Go(LitterSpot,"toilet","重新对准猫砂位置");
            else if(Action=="walk")Retarget();
        }
        RefreshAvailability();
        if(State.CareRequest==kind)FinishRequest();
        Dirty=true;
    }
    public static int SupplyLayers(double quantity)=>(int)Math.Ceiling(Math.Clamp(quantity,0,100)/20);
    public void Recall() {BeginManualSequence();nightRestUntil=Now+300;var spot=NearestRestSpot(new(Math.Clamp(Nest.X-145,65,Width-65),Grounded?GroundY:Math.Clamp(Nest.Y-70,140,Height-60)));State.X=spot.X;State.Y=spot.Y;NewRest();SetAction("sit",3,"回到小窝附近");}
    public void Demo(string action)
    {
        BeginManualSequence();
        if(action=="eat"){State.Hunger=65; Go(FoodSpot,"eat","演示：去吃饭");}
        else if(action=="drink"){State.Thirst=65;Go(WaterSpot,"drink","演示：去喝水");}
        else if(action=="toilet"){Go(LitterSpot,"toilet","演示：去猫砂盆");}
        else if(action=="sleep")Go(Nest,"sleep","回窝睡觉");
        else {FinishManualSequence();}
    }
}

public static class ClockMath
{
    public static (double Seconds,bool Gap) Elapsed(string priorBoot,double prior,string boot,double current)
    {
        if(priorBoot=="")return(0,false);
        if(priorBoot==boot && current>=prior)return(current-prior,false);
        return(0,true); // Never substitute wall time for missing awake history.
    }
}
