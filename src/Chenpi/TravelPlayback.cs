using System;
using System.Collections.Generic;

namespace Chenpi;

public sealed partial class SpritePlayback
{
    private const double TravelTick=1d/120;
    private sealed record TravelPoint(SpriteFrame Frame,double Distance,string Pose,string Destination,string Current,double Started);
    private sealed record TravelPlan(string Action,double From,double To,double Began,List<TravelPoint> Points);
    private TravelPlan? travel;

    // Spend space, not animation time: retain complete stand/start/stop clips,
    // fit their root displacement to the leg, and add only whole walk cycles.
    public (double X,bool Complete)? TravelTo(string action,double from,double to,double now,bool left)
    {
        if(!videoGraph||action is not ("run" or "walk" or "request-walk" or "guide-walk"))return null;
        if(travel is not null&&(travel.Action!=action||Math.Abs(travel.To-to)>.01))CancelTravel(now);
        if(travel is null)
        {
            if(Math.Abs(to-from)<.01)return(to,true);
            Sample(action,now,left);
            // Pickup landing must finish before the motion graph owns a leg.
            if(careExit.Length>0)return(from,false);
            bool running=action=="run"&&HasCompletion;
            var minimum=PredictTravel(now,left,0,running:running);
            double distance=Math.Abs(to-from),baseDistance=minimum[^1].Distance;
            string loop=running?(left?"video-112":"video-109"):(left?"video-14":"video-right");
            double cycleDistance=Math.Abs(FrameVelocity(loop,0))*clips[loop].Duration;
            string? vocal=null;double vocalDistance=0;
            int interval=WalkingCallEvery?.Invoke()??3;
            if(!running&&HasExpressions&&interval>0&&++travelLegs%interval==0&&current is not ("video-right" or "video-14"))
            {
                string id=left?"video-87":"video-86";double required=Math.Abs(FrameVelocity(id,0))*clips[id].Duration;
                if(distance>=baseDistance+required){vocal=id;vocalDistance=required;}
            }
            int loops=(int)Math.Ceiling(Math.Max(0,distance-baseDistance-vocalDistance)/Math.Max(1,cycleDistance));
            // A run includes at least one complete authored run cycle.
            if(running)loops=Math.Max(1,loops);
            travel=new(action,from,to,now,loops==0&&vocal is null?minimum:PredictTravel(now,left,loops,vocal,running));
        }
        var point=TravelFrame(now);double total=travel.Points[^1].Distance;
        bool complete=now-travel.Began>=(travel.Points.Count-1)*TravelTick;
        double ratio=complete?1:Math.Clamp(point.Distance/Math.Max(.001,total),0,1);
        return(travel.From+(travel.To-travel.From)*ratio,complete);
    }

    private List<TravelPoint> PredictTravel(double now,bool left,int loops,string? vocal=null,bool running=false)
    {
        // SampleVideo mutates only these value fields. It never writes shared
        // clips/pending queues, so prediction cannot advance the live renderer.
        var preview=(SpritePlayback)MemberwiseClone();preview.travel=null;preview.walkVocalClip=vocal;
        var result=new List<TravelPoint>();double distance=0,loopAt=-1;
        string gait=running?(left?"video-112":"video-109"):(left?"video-14":"video-right"),stand=left?"SL":"SR";
        double stopAt=double.PositiveInfinity;
        for(int i=0;i<120*300;i++)
        {
            double at=now+i*TravelTick;
            var frame=preview.SampleVideo(at>=stopAt?"guide-stop":running?"run":"walk",at,left)!.Value;
            if(loopAt<0&&(frame.Clip==gait||frame.Clip==vocal))
            {
                loopAt=at;stopAt=at+loops*clips[gait].Duration+(vocal is null?0:clips[vocal].Duration);
                if(loops==0&&vocal is null)frame=preview.SampleVideo("guide-stop",at,left)!.Value;
            }
            // Turning changes orientation in place; only authored steps spend distance.
            double velocity=preview.FrameVelocity(frame.Clip,Math.Clamp((at-preview.started)/frame.Definition.Duration,0,1));
            double speed=frame.Clip is "video-15" or "video-16"||velocity*(left?-1:1)<0?0:Math.Abs(velocity);
            if(i>0)distance+=speed*TravelTick;
            result.Add(new(frame,distance,preview.pose,preview.destination,preview.current,preview.started));
            if(at>=stopAt&&preview.pose==stand&&preview.current.Length==0)return result;
        }
        throw new InvalidOperationException("Movement graph did not reach a standing endpoint.");
    }

    private TravelPoint TravelFrame(double now)
    {
        var plan=travel!;double index=Math.Max(0,(now-plan.Began)/TravelTick);
        int first=Math.Min(plan.Points.Count-1,(int)index),second=Math.Min(plan.Points.Count-1,first+1);
        var point=plan.Points[first];
        return point with {Distance=point.Distance+(plan.Points[second].Distance-point.Distance)*(index-Math.Floor(index))};
    }
    private void CancelTravel(double now)
    {
        var point=TravelFrame(now);
        pose=point.Pose;destination=point.Destination;current=point.Current;started=point.Started;
        travel=null;
    }
}
