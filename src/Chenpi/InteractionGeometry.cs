using System;

namespace Chenpi;

// Shared logical dimensions for drawing, hit testing, paths and resting clearance.
public static class InteractionGeometry
{
    public const double NestScale=1.6;
    public const double NestWidth=196*NestScale;
    public const double NestHeight=146*NestScale;
    public const double NestHalfWidth=NestWidth/2;
    public const double NestRestOffsetX=20;
    public const double LitterScale=1.7;
    public const double LitterScaleX=2.5;
    public const double LitterHalfWidth=56*LitterScaleX;
    public const double BuryPawOffsetX=105;
    public const double ToiletDepositOffsetX=-12;
    public const double LitterMinimumX=155;
    public const double LitterHeight=62*LitterScale;
    // Measured on the cropped prop: the cushion contact line is 68% from its top.
    public const double NestCushionLift=NestHeight*.32;
    public const double LitterSurfaceLift=50;
    public const double PickupLift=24;
    // Supplied right-facing low-head clips: mouth is ~94 units right of root.
    public const double MouthOffsetX=94;
    // Relative to the tray, shared by waste rendering and both care destinations.
    public static Spot LitterClump(int index)=>Math.Clamp(index,0,4) switch {
        0=>new(15,-50),1=>new(35,-47),2=>new(55,-51),3=>new(25,-44),_=>new(45,-43)};
    public static double LitterSupport(double rootX,double trayX)
    {
        return SupportProfile(rootX-trayX,LitterHalfWidth-50,LitterHalfWidth+20,LitterSurfaceLift);
    }
    public static double NestSupport(double rootX,double nestX)
    {
        return SupportProfile(rootX+NestRestOffsetX-nestX,NestHalfWidth-60,NestHalfWidth+20,NestCushionLift);
    }
    public static bool InsideNestSeat(double rootX,double nestX)=>Math.Abs(rootX+NestRestOffsetX-nestX)<=NestHalfWidth-90;
    private static double SupportProfile(double offset,double inner,double outer,double height)
    {double t=Math.Clamp((Math.Abs(offset)-inner)/(outer-inner),0,1);return height*(1-t*t*(3-2*t));}
    public static double SurfaceLift(string clip,double progress,bool inNest,string action)
    {
        double t=Math.Clamp(progress,0,1);t=t*t*(3-2*t);
        if(action=="drag")return PickupLift+(inNest?NestCushionLift:0);
        if(clip=="video-34")return PickupLift*(1-t);
        if(inNest)return NestCushionLift;
        if(action is "toilet" or "bury")return LitterSurfaceLift;
        return 0;
    }
}

// A surface is entered by position while deliberately approaching it, not by
// the first frame of a care clip. The engine retains ownership across UI hosts.
public sealed class SurfaceSupport
{
    public string? Kind {get;private set;}
    public double Height {get;private set;}
    public double Update(PetEngine engine)
    {
        double x=engine.VisualPosition.X;
        double At(string? kind)=>kind switch {
            "nest"=>InteractionGeometry.NestSupport(x,engine.Nest.X),
            "litter"=>InteractionGeometry.LitterSupport(x,engine.LitterSpot.X),_=>0};
        string? entering=engine.State.Sleeping&&engine.State.SleepingInNest?"nest":
            engine.Action is "toilet" or "bury"?"litter":engine.ApproachingSupport;
        if(entering is not null&&At(entering)>0)Kind=entering;
        Height=engine.Grounded?At(Kind):0;
        if(Height<=0)Kind=null;
        return Height;
    }
}
