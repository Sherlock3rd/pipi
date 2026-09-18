using System;

namespace Chenpi;

// Shared logical dimensions for drawing, hit testing, paths and resting clearance.
public static class InteractionGeometry
{
    public const double LitterScale=1.7;
    public const double LitterHalfWidth=56*LitterScale;
    public const double LitterHeight=62*LitterScale;
    public const double NestCushionLift=54;
    public const double LitterSurfaceLift=50;
    public const double PickupLift=24;
    // Supplied right-facing low-head clips: mouth is ~94 units right of root.
    public const double MouthOffsetX=94;
    public static double NestSupport(double rootX,double nestX)
    {
        double t=Math.Clamp((Math.Abs(rootX+20-nestX)-50)/100,0,1);
        return NestCushionLift*(1-t*t*(3-2*t));
    }
    public static double SurfaceLift(string clip,double progress,bool inNest,string action)
    {
        double t=Math.Clamp(progress,0,1);t=t*t*(3-2*t);
        if(action=="drag")return PickupLift+(inNest?NestCushionLift:0);
        if(clip=="video-34")return PickupLift*(1-t);
        if(inNest)return NestCushionLift;
        if(action is "toilet" or "bury")return clip=="video-26"?LitterSurfaceLift*t:LitterSurfaceLift;
        return 0;
    }
}
