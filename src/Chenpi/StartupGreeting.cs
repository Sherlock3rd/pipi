using System;

namespace Chenpi;

public sealed partial class PetEngine
{
    public bool CompletionEnabled {get;set;}
    public Action<string>? VisualRestorePose {get;set;}
    public Func<string?>? VisualDropPose {get;set;}
    public bool StartupActive {get;private set;}
    public bool PresentationPaused {get;set;}
    public int StartupStage {get;private set;}
    private Action? startupCancel;
    private double startupCenter,startupLeft,startupRight;
    private int startupTrips;
    private bool dropIntoNest;
    public static bool StartupEligible(PetState state,bool firstRun,bool autoStart,string boot)=>
        firstRun&&!state.StartupSeen||autoStart&&state.StartupBoot!=boot;
    public bool BeginStartup(bool firstRun=false,bool autoStart=false,string boot="",bool preview=false)
    {
        if(!CompletionEnabled||StartupActive||!preview&&!StartupEligible(State,firstRun,autoStart,boot))return false;
        CancelDebugBehavior();Sleep();VisualRestorePose?.Invoke("C");
        startupCenter=NearestRestSpot(new(Width/2,GroundY)).X;
        double span=Settings.Get("intro.span");
        startupLeft=NearestRestSpot(new(startupCenter-span,GroundY)).X;
        startupRight=NearestRestSpot(new(startupCenter+span,GroundY)).X;
        startupTrips=0;StartupStage=0;StartupActive=true;startupCancel=null;
        // Persist before playing: cancellation and a process restart cannot replay it.
        if(!preview){State.StartupSeen=true;State.StartupBoot=boot;Dirty=true;}
        return true;
    }
    private bool QueueStartupCommand(Action command)
    {
        if(!StartupActive)return false;
        startupCancel=command;
        if(Action!="rest-F")SetAction("rest-F",double.MaxValue,"结束开场，沿姿态衔接响应操作");
        return true;
    }
    private void StartupMove(double x,bool run)
    {
        target=OnGround(new(x,GroundY));FacingLeft=x<State.X;arrival="intro";
        SetAction(run?"run":"walk",double.MaxValue,"开场：前往安全空位");
    }
    private bool UpdateStartup(double dt)
    {
        if(!StartupActive)return false;
        if(startupCancel is Action command)
        {
            if(VisualPoseReady?.Invoke("F",Now)==false)return true;
            StartupActive=false;startupCancel=null;SetAction("idle",RestDelay,"已取消开场");command();return true;
        }
        if(StartupStage==0)
        {
            if(ActionTime>=Settings.Get("intro.sleep")){StartupStage=1;StartupMove(startupCenter,true);}
            return true;
        }
        if(StartupStage<=4)
        {
            // Furniture/layout may change while the greeting is running.
            double intended=StartupStage switch {2=>startupLeft,3=>startupRight,_=>startupCenter};
            target=NearestRestSpot(new(intended,GroundY));
            if(Math.Abs(target.X-State.X)>.1)FacingLeft=target.X<State.X;
            MoveTowards(target,WalkSpeed*dt);
            if(!MovementComplete||Math.Abs(State.X-target.X)>.1)return true;
            if(StartupStage==1){StartupStage=2;StartupMove(startupLeft,false);}
            else if(StartupStage==2){StartupStage=3;StartupMove(startupRight,false);}
            else if(StartupStage==3&&++startupTrips<Settings.Get("intro.trips")){StartupStage=2;StartupMove(startupLeft,false);}
            else if(StartupStage==3){StartupStage=4;StartupMove(startupCenter,false);}
            else {StartupStage=5;SetAction("rest-SF",double.MaxValue,"开场：转正面站立");}
            return true;
        }
        if(StartupStage==5)
        {
            if(VisualPoseReady?.Invoke("SF",Now)==false)return true;
            StartupStage=6;SetAction("expr-116",4,"开场：朝前蹭头");return true;
        }
        if(StartupStage==6&&ActionTime>=duration){StartupStage=7;SetAction("expr-117",4,"开场：连续叫三次");}
        else if(StartupStage==7&&ActionTime>=duration){StartupStage=8;SetAction("rest-F",double.MaxValue,"开场：坐定");}
        else if(StartupStage==8&&VisualPoseReady?.Invoke("F",Now)!=false)
        {StartupActive=false;NewRest();SetAction("idle",RestDelay,"开场完成，正常活动");}
        return true;
    }
}
