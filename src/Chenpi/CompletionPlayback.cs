using System;
using System.Collections.Generic;

namespace Chenpi;

public sealed partial class SpritePlayback
{
    public double RunSpeedRight {get;set;}=137.5;
    public double RunSpeedLeft {get;set;}=129.6;
    private string? liftedBase;
    private double lastSampleAt;
    public string? DropPose=>liftedBase;
    private static int PickupId(string p)=>p switch {"D"=>125,"A"=>128,"B"=>131,"X"=>134,"M"=>137,_=>0};
    private IEnumerable<(string From,string To,string Clip)> CarryEdges()
    {
        foreach(string p in new[]{"D","A","B","X","M"})
        {int n=PickupId(p);yield return(p,"H_"+p,n.ToString());yield return("H_"+p,p,(n+2).ToString());}
    }
    private SpriteFrame? SampleAuthoredCarry(string action,double now)
    {
        lastSampleAt=now;
        if(!HasCompletion)return null;
        if(action=="drag"&&liftedBase is null)
        {
            string p=PickupId(destination)>0?destination:pose;
            if(PickupId(p)>0)liftedBase=p;
        }
        if(liftedBase is not string basePose)return null;
        if(action is "drag" or "land")return SampleVideo("rest-"+(action=="drag"?"H_":"")+basePose,now,false);
        // Ordinary input can cancel after the authored release, never invent a
        // front-seated pickup for a lying cat.
        liftedBase=null;
        return null;
    }
    private double? ReleaseDuration()
    {
        if(liftedBase is not string p)return null;
        double remaining=current.Length>0&&!clips[current].Loop?Math.Max(0,clips[current].Duration-(lastSampleAt-started)):0;
        return remaining+clips["video-"+(PickupId(p)+2)].Duration;
    }
    public static double? AuthoredCarryLift(SpriteFrame frame)
    {
        if(!frame.Clip.StartsWith("video-")||!int.TryParse(frame.Clip.AsSpan(6),out int n)||n<125||n>139)return null;
        double t=frame.Index/(double)Math.Max(1,frame.Definition.Count-1);t=t*t*(3-2*t);
        return InteractionGeometry.PickupLift*(((n-125)%3) switch {0=>t,1=>1,_=>1-t});
    }
}
