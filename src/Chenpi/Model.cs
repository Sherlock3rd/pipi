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
    private bool moveAfterWake;
    private int clickStreak;
    private double hoverSeconds;
    private bool hoverTriggered;
    private Spot rubTarget;
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
    public Spot WandHome=>new(Math.Clamp(Nest.X+25,60,Width-65),Math.Max(55,Nest.Y-167));
    public bool CanDropInNest(Spot pointer)=>new Spot(State.X,State.Y).Distance(Nest)<82
        ||(pointer.X>=Nest.X-87&&pointer.X<=Nest.X+87&&pointer.Y>=Nest.Y-129&&pointer.Y<=Nest.Y+25);

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
        Spot Clamp(Spot p,double side,double top,double bottom)=>new(Math.Clamp(p.X,side,width-side),Math.Clamp(p.Y,top,height-bottom));
        Nest=Clamp(!reset&&State.NestPosition is Spot n?n:new(width-100,height-68),94,145,55);
        FoodSpot=Clamp(!reset&&State.FoodPosition is Spot f?f:new(width-243,height-56),48,50,40);
        WaterSpot=Clamp(!reset&&State.WaterPosition is Spot w?w:new(width-336,height-56),48,50,40);
        LitterSpot=Clamp(!reset&&State.LitterPosition is Spot l?l:new(width-443,height-56),60,50,40);
        RememberLayout();
        if(State.X<0||reset){State.X=Nest.X-115;State.Y=Nest.Y-70;}
        State.X=Math.Clamp(State.X,65,width-65);State.Y=Math.Clamp(State.Y,140,height-60);
        if(State.Sleeping&&State.SleepingInNest){State.X=Nest.X;State.Y=Nest.Y;}
        Retarget();
    }
    private void RememberLayout(){State.NestPosition=Nest;State.FoodPosition=FoodSpot;State.WaterPosition=WaterSpot;State.LitterPosition=LitterSpot;Dirty=true;}
    public Spot ObjectPosition(string kind)=>kind switch {"nest"=>Nest,"food"=>FoodSpot,"water"=>WaterSpot,_=>LitterSpot};
    public void MoveObject(string kind,Spot position)
    {
        double side=kind=="nest"?94:kind=="litter"?60:48;
        position=new Spot(Math.Clamp(position.X,side,Width-side),Math.Clamp(position.Y,kind=="nest"?145:50,Height-(kind=="nest"?55:40)));
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
    }

    public PetEngine(PetState state, int? seed = null)
    {
        State = state; random = seed.HasValue?new Random(seed.Value):new Random();
        if(State.RestDuration<120||State.RestDuration>3600)NewRest();
        InitializeCare();
        tree = new Selector(
            Branch(()=>ReadyRequest() is not null, ()=>BeginRequest(ReadyRequest()!)),
            Branch(()=>DueCare() is not null, ()=>StartCare(DueCare()!)),
            Branch(()=>State.StillSeconds>=300, SleepHere),
            Branch(()=>State.RestElapsed>=State.RestDuration, Roam),
            Branch(()=>State.Energy<24 || ((localHour>=23 || localHour<6) && Now>nightRestUntil && State.Energy<90), SleepHere),
            new BehaviorAction(()=>SetAction(random.NextDouble()<.7?"sit":"idle",1,"在这里待一会儿"))
        );
        toyTree=new Selector(Branch(()=>ToyWithinCatchRange,CatchToy),new BehaviorAction(ChaseToy));
        if(state.Sleeping) {bool inNest=state.SleepingInNest;CancelCareRequest();sleepGrace=Now+NestSleepGrace;SetAction("sleep",double.MaxValue,"继续睡觉");state.SleepingInNest=inNest;}
    }
    private void NewRest(){State.RestDuration=random.Next(120,3601);State.RestElapsed=0;State.StillSeconds=0;Dirty=true;}
    private void Roam()
    {
        double angle=random.NextDouble()*Math.PI*2,distance=random.Next(110,261);
        var spot=new Spot(Math.Clamp(State.X+Math.Cos(angle)*distance,70,Width-70),Math.Clamp(State.Y+Math.Sin(angle)*distance*.55,140,Height-65));
        if(spot.Distance(new(State.X,State.Y))<50)spot=new Spot(Math.Clamp(State.X+(State.X>Width/2?-150:150),70,Width-70),State.Y);
        Go(spot,"settle","换个舒服的地方");
    }
    public void ObservePointer(double seconds,bool overCat,Spot cursor)
    {
        guidePointer=cursor;pointerKnown=true;
        if(!Holding&&!ToyHeld&&State.CareRequest is not null)
        {if(overCat&&!State.Guiding&&Action.StartsWith("request-"))BeginGuide();hoverSeconds=0;hoverTriggered=false;return;}
        if(!overCat||Holding||ToyHeld||manualSequence||Action is "drag" or "walk" or "eat" or "drink" or "toilet" or "bury" or "sleep")
        {hoverSeconds=0;hoverTriggered=false;return;}
        if(Action=="rub"){rubTarget=cursor;return;}
        if(hoverTriggered)return;
        hoverSeconds+=Math.Max(0,seconds);
        if(hoverSeconds>=5){hoverTriggered=true;rubTarget=cursor;LastInteraction=Now;State.StillSeconds=0;SetAction("rub",4,"悬停蹭鼠标");}
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
        var desired=new Spot(Math.Clamp(ToyTip.X,65,Width-65),Math.Clamp(ToyTip.Y+65,140,Height-60));
        MoveTowards(desired,ToyRunSpeed*toyStep);
        if(ToyWithinCatchRange)CatchToy();
    }
    private void CatchToy()
    {
        if(Action!="toy-bat")SetAction("toy-bat",double.MaxValue,"贴近抓逗猫棒");
    }
    private void MoveTowards(Spot destination,double step)
    {
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
        dt=Math.Clamp(dt,0,0.12); Now+=dt; localHour=hour;
        if(Holding) return;
        ActionTime+=dt;
        if(Action=="drag") return;
        if(ToyOverlaps){TickToy(dt);return;}
        if(Action.StartsWith("toy-")){NewRest();SetAction("sit",1,"等主人靠近一点");}
        if(UpdateCareFlow(dt))return;
        if(CanStartCare())
        {
            string? request=ReadyRequest();
            if(request is not null){BeginRequest(request);UpdateCareFlow(dt);return;}
            string? due=DueCare();if(due is not null)StartCare(due);
        }
        if(Action=="rub")
        {
            FacingLeft=rubTarget.X<State.X;
            MoveTowards(new Spot(Math.Clamp(rubTarget.X+(FacingLeft?18:-18),65,Width-65),Math.Clamp(rubTarget.Y+82,140,Height-60)),24*dt);
        }
        if(Action=="walk")
        {
            var here=new Spot(State.X,State.Y); var distance=here.Distance(target);
            if(Math.Abs(target.X-State.X)>.1)FacingLeft=target.X<State.X;
            if(CanAdvanceMovement?.Invoke(Action,Now,FacingLeft)==false)return;
            if(distance<2) { State.X=target.X;State.Y=target.Y; Arrive(); }
            else MoveTowards(target,WalkSpeed*dt);
            return;
        }
        if(Action is "idle" or "sit"&&VisualVelocity?.Invoke(Action,Now,FacingLeft) is double drift)
            State.X=Math.Clamp(State.X+drift*dt,65,Width-65);
        State.RestElapsed+=dt;State.StillSeconds+=dt;
        if(State.StillSeconds>=300&&Action is "idle" or "sit")SleepHere();
        if(Action=="sleep")
        {
            State.Sleeping=true;
            if(Now>sleepGrace&&State.RestElapsed>=State.RestDuration) {Wake();moveAfterWake=true;}
            return;
        }
        if(Action is "eat" or "drink")
        {
            int completed=(int)(ActionTime/1.1);
            while(bites<completed && bites<4)
            {
                bool food=Action=="eat";
                if(bites==0&&(food?State.Food:State.Water)>0)ScheduleNext(food?"food":"water");
                Consume(food,5);bites++;
            }
            if((Action=="eat" && State.Food<=0)||(Action=="drink" && State.Water<=0)) duration=ActionTime;
        }
        if(ActionTime<duration)return;
        if(Action=="wake"&&moveAfterWake){moveAfterWake=false;Roam();return;}
        if(Action=="land"){NewRest();SetAction("sit",1,"这里也很舒服");return;}
        if(Action=="toilet") {State.Bladder=0; State.Litter=Math.Clamp(State.Litter+20,0,100);ScheduleNext("litter");RefreshAvailability();Dirty=true;SetAction("bury",3,"把猫砂埋好");return;}
        if(Action=="bury") {Go(new Spot(Math.Clamp(LitterSpot.X-80,65,Width-65),Math.Clamp(LitterSpot.Y-65,140,Height-60)),"settle","收拾好啦");return;}
        if(manualSequence){FinishManualSequence();return;}
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
        moveAfterWake=false;
        target=destination;if(Math.Abs(target.X-State.X)>.1)FacingLeft=target.X<State.X;
        arrival=next;SetAction("walk",double.MaxValue,reason);
    }
    private void Arrive()
    {
        NewRest();
        if((arrival=="eat"&&State.Food<=0)||(arrival=="drink"&&State.Water<=0)||(arrival=="toilet"&&State.Litter>=100))
        {if(manualSequence)FinishManualSequence();else SetAction("sit",1,"目标物品不可用");return;}
        if(arrival=="sleep")Sleep(false);
        else if(arrival=="settle")
        {if(manualSequence){FinishManualSequence();return;}if(random.NextDouble()<.15)SleepHere();else SetAction(random.NextDouble()<.65?"sit":"idle",1,"找到舒服的地方啦");}
        else SetAction(arrival,arrival is "eat" or "drink" ? 4.7 : arrival=="toilet" ? 4 : arrival=="bury" ? 3 : 2,Reason);
    }
    private void SetAction(string action,double seconds,string reason)
    {Action=action;ActionTime=0;ActionRevision++;duration=seconds;bites=0;Reason=reason;State.Sleeping=action=="sleep";if(!State.Sleeping)State.SleepingInNest=false;Dirty=true;}
    public void Interact()
    {
        if(State.CareRequest is not null){BeginGuide();return;}
        manualSequence=false;
        clickStreak=Now-LastInteraction<6?clickStreak+1:1;LastInteraction=Now;State.StillSeconds=0;
        if(Action=="sleep"){Wake();moveAfterWake=random.NextDouble()<.3;}
        else {if(State.RestElapsed>=State.RestDuration)NewRest();SetAction(clickStreak%3==0?"roll":clickStreak%3==2?"paw":"pet",3,"点击互动");}
    }
    private void BeginManualSequence()
    {CancelCareRequest();Holding=false;ToyHeld=false;manualSequence=true;careResumeAfter=0;hoverSeconds=0;hoverTriggered=false;moveAfterWake=false;LastInteraction=Now;State.StillSeconds=0;}
    private void FinishManualSequence()
    {manualSequence=false;careResumeAfter=Now+3;NewRest();SetAction("sit",1,"手动动作完成，稍作停留");}
    public void BeginDrag() {BeginManualSequence();manualSequence=false;SetAction("drag",double.MaxValue,"被主人拎起来啦");}
    public void Drop(bool inNest)
    {LastInteraction=Now;if(inNest)Sleep();else SetAction("land",0.7,"稳稳落地");}
    public void Sleep(bool newRest=true)
    {CancelCareRequest();manualSequence=false;Holding=false;ToyHeld=false;State.X=Nest.X;State.Y=Nest.Y;if(newRest)NewRest();sleepGrace=Now+NestSleepGrace;SetAction("sleep",double.MaxValue,"猫窝睡眠");State.SleepingInNest=true;}
    private void SleepHere(){sleepGrace=Now+NestSleepGrace;SetAction("sleep",double.MaxValue,"就在这里打个盹");State.SleepingInNest=false;}
    public void Wake() {nightRestUntil=Now+300;State.StillSeconds=0;State.RestElapsed=0;moveAfterWake=false;SetAction("wake",2.0,"唤醒伸展");}
    public void Refill(string kind)
    {
        LastInteraction=Now;
        if(kind=="food")State.Food=Math.Min(100,(SupplyLayers(State.Food)+1)*20);
        else if(kind=="water")State.Water=Math.Min(100,(SupplyLayers(State.Water)+1)*20);
        else State.Litter=Math.Max(0,(SupplyLayers(State.Litter)-1)*20);
        RefreshAvailability();
        if(State.CareRequest==kind)FinishRequest();
        Dirty=true;
    }
    public static int SupplyLayers(double quantity)=>(int)Math.Ceiling(Math.Clamp(quantity,0,100)/20);
    public void Recall() {BeginManualSequence();nightRestUntil=Now+300;State.X=Math.Clamp(Nest.X-115,65,Width-65);State.Y=Math.Clamp(Nest.Y-70,140,Height-60);NewRest();SetAction("sit",3,"回到小窝附近");}
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
