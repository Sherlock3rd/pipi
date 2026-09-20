using System;

namespace Chenpi;

public sealed partial class PetEngine
{
    public bool ExpressionsEnabled {get;set;}
    public Func<string,double,double,bool>? VisualWallRestComplete {get;set;}
    public Func<string,double,bool>? VisualPoseReady {get;set;}
    public double WallRightOffset {get;set;}=70;
    public double WallLeftOffset {get;set;}=-70;
    public double RelaxedHalfWidth {get;set;}=170;
    public double RestDelay=>ExpressionsEnabled?Settings.Get("sleep.delay"):QuietSleepDelay;
    public string RelaxedPose {get;private set;}="";
    public int RelaxedClickCount {get;private set;}
    private double clickWindow=-1,nextRelaxation;
    private int pendingReaction;
    private bool restReady;
    private double nextSeatedCall=120;
    private bool IsRelaxing=>Action.StartsWith("rest-")||Action.StartsWith("expr-");
    private static bool LyingPose(string p)=>p is "C" or "D" or "A" or "B" or "X" or "M";
    private void RestInPose(string p,bool asleep=true)
    {
        RelaxedPose=p;restReady=false;nextRelaxation=0;
        SetAction("rest-"+p,double.MaxValue,"放松休息");State.Sleeping=asleep&&LyingPose(p);
        State.SleepingInNest=false;State.RestPose=p;
    }
    private bool BeginRelaxedSleep()
    {
        if(!ExpressionsEnabled)return false;
        RestInPose((CompletionEnabled?Settings.Choose(random,"pose.C","pose.D","pose.A","pose.B","pose.X","pose.M"):Settings.Choose(random,"pose.D","pose.A","pose.B","pose.X","pose.M"))[5..]);return true;
    }
    private bool UpdateRelaxation()
    {
        if(!ExpressionsEnabled||!IsRelaxing)return false;
        if(RelaxedPose is "HR" or "HL"&&Math.Abs(State.X-(RelaxedPose=="HR"?Width-WallRightOffset:-WallLeftOffset))>2)
        {RestInPose("D");return true;}
        if(Action.StartsWith("expr-"))
        {
            if(ActionTime<duration)return true;
            if(Action is "expr-119" or "expr-120"){if(LeaveOccupiedRestSpot())return true;RestInPose("D");return true;}
            if(!LyingPose(RelaxedPose)){SetAction("idle",RestDelay,"伸展后安静休息");return true;}
            RestInPose(RelaxedPose);return true;
        }
        if(VisualPoseReady?.Invoke(RelaxedPose,Now)==false)return true;
        if(!restReady){restReady=true;nextRelaxation=Now+(RelaxedPose is "HR" or "HL"?Settings.Get("wall.duration"):Settings.Range(random,"relax"));}
        if(pendingReaction>0)
        {
            int level=pendingReaction;pendingReaction=0;
            if(level==3){RelaxedClickCount=0;clickWindow=-1;RestInPose("SR",false);nextRelaxation=Now+2;return true;}
            int id=RelaxedPose switch {"A"=>level==1?68:69,"B"=>level==1?72:73,"X"=>level==1?76:77,_=>0};
            if(id>0){SetAction("expr-"+id,4,"躺着回应点击");return true;}
        }
        if(RelaxedPose is "SR" or "SL"){NewRest();if(random.NextDouble()*100<Settings.Get("stand.stretchChance"))SetAction(RelaxedPose=="SR"?"expr-93":"expr-94",4,"起身伸展");else SetAction("idle",RestDelay,"起身后安静休息");return true;}
        if(clickWindow>=0&&Now-clickWindow<Settings.Get("click.window")-1e-8)return true;
        if(clickWindow>=0){clickWindow=-1;RelaxedClickCount=0;}
        if(Now<nextRelaxation)return true;
        if(RelaxedPose is "HR" or "HL"){if(VisualWallRestComplete?.Invoke(RelaxedPose,Now,nextRelaxation)==false)return true;Go(NearestRestSpot(new(State.X,GroundY)),"settle","扶墙结束，落地后到安全空位休息");return true;}
        if(State.RestElapsed>=State.RestDuration)NewRest();
        if(RelaxedPose=="M"){nextRelaxation=Now+120;return true;}
        if(RelaxedPose=="C"){RestInPose(random.Next(2)==0?"D":"A");return true;}
        // Most idle time remains in breathing poses. Gestures are infrequent,
        // complete once, and return to the same pose without getting up.
        if(random.NextDouble()*100<Settings.Get("relax.gesture"))
        {
            int[] ids=RelaxedPose switch {"D"=>new[]{79,89,95},"A"=>new[]{80,90,96},"B"=>new[]{81,91,97},_=>new[]{82,92,98}};
            SetAction("expr-"+Settings.Choose(random,"gesture."+ids[0],"gesture."+ids[1],"gesture."+ids[2])[8..],4,"安静伸展");return true;
        }
        string next=RelaxedPose switch {"D"=>CompletionEnabled&&random.Next(2)==0?"C":"A","A"=>CompletionEnabled?Settings.Choose(random,"change.C","change.B","change.X","change.M")[7..]:Settings.Choose(random,"change.B","change.X","change.M")[7..],"B" or "X"=>"A",_=>"D"};
        RestInPose(next);return true;
    }
    private bool InteractRelaxed()
    {
        if(!ExpressionsEnabled||!IsRelaxing)return false;
        LastInteraction=Now;State.StillSeconds=0;
        if(RelaxedPose=="M"){pendingReaction=0;RelaxedClickCount=0;clickWindow=-1;RestInPose("A",false);return true;}
        if(RelaxedPose is not ("A" or "B" or "X")){RestInPose(RelaxedPose=="HL"?"SL":"SR",false);return true;}
        if(clickWindow<0||Now-clickWindow>=Settings.Get("click.window")-1e-8){clickWindow=Now;RelaxedClickCount=0;}
        RelaxedClickCount=Math.Min(3,RelaxedClickCount+1);
        pendingReaction=Math.Max(pendingReaction,RelaxedClickCount);
        return true;
    }
    public void PreviewRest(string pose)
    {
        if(!ExpressionsEnabled||!LyingPose(pose)&&pose is not ("HR" or "HL"))return;
        if(pose is "HR" or "HL")State.X=pose=="HR"?Width-WallRightOffset:-WallLeftOffset;
        CancelCareRequest();NewRest();sleepGrace=Now+Settings.Get("sleep.grace");RestInPose(pose);
    }
    public void RestoreRelaxedSleep()
    {
        if(ExpressionsEnabled&&State.Sleeping&&!State.SleepingInNest&&LyingPose(State.RestPose))RestInPose(State.RestPose);
    }
    private bool TrySeatedCall()
    {
        if(!ExpressionsEnabled||Action is not ("idle" or "sit")||Now<nextSeatedCall||State.StillSeconds<5||State.StillSeconds>12)return false;
        if(VisualPoseReady?.Invoke("F",Now)==false)return false;
        nextSeatedCall=Now+Settings.Get("sit.callCooldown");RelaxedPose="F";SetAction("expr-88",4,"轻声回应");return true;
    }
}
