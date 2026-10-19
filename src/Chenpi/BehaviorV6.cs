using System;

namespace Chenpi;

public enum CatBodyPart { Head, Back, Belly, Paw, Tail }

public sealed partial class PetEngine
{
    public bool BodyInteractionsEnabled {get;set;}
    public bool BurialV6Enabled {get;set;}
    public bool ToyGrabsEnabled {get;set;}
    public bool HighToyGrabsEnabled {get;set;}
    public Func<string>? VisualInteractionPose {get;set;}
    public int ToyGrabClip {get;private set;}
    public double ToyHeight=>State.Y-Support.Height-ToyTip.Y;
    // Calibrated from the authored reaching paw, relative to the ground root.
    public const double LowToyReachHeight=85;
    public bool WantsHighToyGrab=>HighToyGrabsEnabled&&ToyHeight>LowToyReachHeight;
    public double ToyReachOffset=>FacingLeft?(WantsHighToyGrab?-114:-147):(WantsHighToyGrab?82:149);
    private bool UpdateToyGrab()
    {
        if(ToyGrabClip==0)return false;
        if(Action!="expr-"+ToyGrabClip){ToyGrabClip=0;return false;}
        // Hold the authored side and finish the landing before target changes,
        // height changes or returning the wand can start another movement.
        LastInteraction=Now;State.StillSeconds=0;
        if(ActionTime+1e-8<duration)return true;
        string pose=SpritePlayback.ExpressionPose(ToyGrabClip);ToyGrabClip=0;
        RestInPose(pose,false);VisualPoseReady?.Invoke(pose,Now);
        if(!ToyOverlaps){NewRest();SetAction("sit",1,"抓取结束，安静休息");return true;}
        return false;
    }
    public string BurialStage {get;private set;}="";
    public string LastBurialExit {get;private set;}="";
    public double? JumpSupportHeight {get;private set;}
    private int bodyClip;
    private bool bodyPreparing;
    private string bodyPose="F";
    private int burialSlot=-1;
    private bool jumpLeft,burialCleaned;
    private bool finalBurialExit,bodyResting;
    private double jumpStart,jumpEnd,jumpStartLift;

    public void Interact(CatBodyPart part,bool? facingLeft=null)
    {
        if(!BodyInteractionsEnabled){Interact();return;}
        if(QueueStartupCommand(()=>Interact(part,facingLeft)))return;
        if(State.CareRequest is not null){BeginGuide();return;}
        // A light click must not abort consumption, scratch/landing, or queue a
        // delayed show after a critical action. Drag still uses BeginManualSequence.
        if(Action is "eat" or "drink" or "toilet" or "bury" or "land" or "wake" || BurialStage.Length>0 || ToyGrabClip!=0)return;
        if(bodyClip!=0||bodyPreparing)return;
        CancelDebugBehavior();ResetMovementChoice();
        LastInteraction=Now;State.StillSeconds=0;manualSequence=false;
        if(IsRelaxing&&LyingPose(RelaxedPose)){InteractRelaxed(part);return;}
        if(State.Sleeping||Action=="sleep"){Wake();return;}
        if(Action.StartsWith("expr-")&&ActionTime<duration)return;
        string pose=VisualInteractionPose?.Invoke()??(IsRelaxing?RelaxedPose:FacingLeft?"SL":"F");
        if(pose is "HR" or "HL"){RestInPose(pose=="HL"?"SL":"SR",false);return;}
        // A transition or lying silhouette is not permission to start a distant
        // pose. In particular 143 can only be selected by the B click ladder.
        if(pose is not ("F" or "I" or "SF" or "SR" or "SL" or "WR" or "WL" or "RR" or "RL"))return;
        bool left=pose is "SL" or "WL" or "RL" || facingLeft==true;
        bool standing=pose is "SR" or "SL" or "WR" or "WL" or "RR" or "RL";
        bodyClip=part switch {
            CatBodyPart.Head=>standing?(left?142:141):140,
            CatBodyPart.Paw=>standing?(left?146:145):144,
            CatBodyPart.Back=>left?142:141,
            CatBodyPart.Tail=>left?146:145,
            _=>standing?(left?142:141):144};
        bodyPose=SpritePlayback.ExpressionPose(bodyClip);bodyPreparing=true;
        bodyResting=false;
        RestInPose(bodyPose,false);
    }

    private bool UpdateBodyInteraction()
    {
        if(bodyClip==0&&!bodyPreparing)return false;
        if(bodyClip!=0&&!bodyPreparing)
        {
            if(ActionTime<duration)return true;
            bodyClip=0;RestInPose(bodyPose,false);
            NewRest();bodyResting=bodyPose is "SR" or "SL";
            if(bodyPose=="F")SetAction("idle",RestDelay,"互动结束，安静休息");return true;
        }
        if(VisualPoseReady?.Invoke(bodyPose,Now)==false)return true;
        bodyPreparing=false;SetAction("expr-"+bodyClip,bodyClip==143?145d/24:97d/24,"回应身体部位点击");
        return true;
    }

    private void CancelBodyInteraction(){bodyClip=0;bodyPreparing=false;bodyResting=false;pendingBellyReaction=false;}
    private void CancelBurialV6(){BurialStage="";burialSlot=-1;JumpSupportHeight=null;burialCleaned=false;finalBurialExit=false;}
    private bool BeginBurialV6()
    {
        if(!BurialV6Enabled)return false;
        LastBurialExit="";finalBurialExit=false;
        burialSlot=SupplyLayers(State.Litter)-1;BurialStage="exit-ready";burialCleaned=false;
        RestInPose("SR",false);return true;
    }
    private bool BeginPostBurialExit()
    {
        // Called once, after the complete 29/30/31 sequence (including paw down).
        LastBurialExit="walk";
        if(!BurialV6Enabled||random.NextDouble()*100>=Settings.Get("litter.exitJumpChance"))return false;
        var exit=NearestRestSpot(new(State.X,GroundY));
        if(!IsClearRestSpot(exit.X)||Math.Abs(exit.X-State.X)>550||InteractionGeometry.LitterSupport(State.X,LitterSpot.X)<=0)return false;
        LastBurialExit="jump";finalBurialExit=true;burialSlot=-1;burialCleaned=false;
        BurialStage="final-exit-ready";RestInPose("SR",false);return true;
    }
    private Spot SniffDestination()
    {
        var clump=InteractionGeometry.LitterClump(burialSlot);
        // Nose offsets measured in the middle of 149/150, in logical pixels.
        return OnGround(new(LitterSpot.X+clump.X-(jumpLeft?102.18:-99.36),GroundY));
    }
    private bool UpdateBurialV6(double dt)
    {
        if(BurialStage.Length==0)return false;
        if(!finalBurialExit&&SupplyLayers(State.Litter)<=burialSlot)burialCleaned=true;
        if(burialCleaned&&BurialStage!="jump")
        {CancelBurialV6();Go(NearestRestSpot(new(State.X,GroundY)),"settle","已经清理好了");return true;}
        if(BurialStage is "exit-ready" or "final-exit-ready")
        {
            var exit=NearestRestSpot(new(State.X,GroundY));
            if(!IsClearRestSpot(exit.X)||Math.Abs(exit.X-State.X)>550||finalBurialExit&&InteractionGeometry.LitterSupport(State.X,LitterSpot.X)<=0)
            {
                if(finalBurialExit){CancelBurialV6();LastBurialExit="walk";settleAfterCare=CompletionEnabled;Go(exit,"settle","落点不可用，走出猫砂盆");}
                return true;
            }
            jumpLeft=exit.X<State.X;jumpEnd=exit.X;
            var pose=jumpLeft?"SL":"SR";
            if(Action!="rest-"+pose)RestInPose(pose,false);
            if(VisualPoseReady?.Invoke(pose,Now)==false)return true;
            FacingLeft=jumpLeft;jumpStart=State.X;jumpStartLift=InteractionGeometry.LitterSupport(State.X,LitterSpot.X);JumpSupportHeight=jumpStartLift;
            BurialStage=finalBurialExit?"final-jump":"jump";SetAction(jumpLeft?"expr-148":"expr-147",97d/24,"跳出猫砂盆");return true;
        }
        if(BurialStage is "jump" or "final-jump")
        {
            // Native takeoff/first landing frames: 38..64. Hold the root during
            // anticipation and recovery; preserve the video's vertical arc.
            double t=Math.Clamp((ActionTime*24-38)/26,0,1);double smooth=t*t*(3-2*t);
            State.X=jumpStart+(jumpEnd-jumpStart)*smooth;
            JumpSupportHeight=jumpStartLift*(1-smooth);
            if(ActionTime<duration)return true;
            JumpSupportHeight=null;
            if(finalBurialExit)
            {
                CancelBurialV6();manualSequence=false;careResumeAfter=Now+3;NewRest();
                if(!IsClearRestSpot(State.X)){settleAfterCare=CompletionEnabled;Go(NearestRestSpot(new(State.X,GroundY)),"settle","落地后避开搬来的物品");}
                else RestInPose("D");
                return true;
            }
            if(burialCleaned)
            {CancelBurialV6();RestInPose(jumpLeft?"SL":"SR",false);return true;}
            BurialStage="return";Go(SniffDestination(),"v6-sniff","回头走向刚留下的便便");return true;
        }
        if(BurialStage=="return")
        {
            if(Action=="walk")
            {
                target=SniffDestination();if(Math.Abs(target.X-State.X)>.1)FacingLeft=target.X<State.X;
                MoveTowards(target,WalkSpeed*dt);
                if(VisualTravel is not null&&!MovementComplete||Math.Abs(target.X-State.X)>=2)return true;
                if(CanAdvanceMovement?.Invoke(Action,Now,FacingLeft)==false)return true;
                State.X=target.X;
            }
            BurialStage="sniff-ready";RestInPose(jumpLeft?"SR":"SL",false);
        }
        if(BurialStage=="sniff-ready")
        {
            string pose=jumpLeft?"SR":"SL";
            if(VisualPoseReady?.Invoke(pose,Now)==false)return true;
            FacingLeft=!jumpLeft;BurialStage="sniff";SetAction(jumpLeft?"expr-149":"expr-150",97d/24,"嗅闻便便位置");return true;
        }
        if(BurialStage=="sniff")
        {
            if(ActionTime<duration)return true;
            if(Math.Abs(State.X-SniffDestination().X)>1)
            {BurialStage="return";Go(SniffDestination(),"v6-sniff","猫砂盆搬动，重新定位嗅闻");return true;}
            CancelBurialV6();Go(LitterSpot,"bury","嗅闻后换位埋砂");return true;
        }
        return false;
    }
}
