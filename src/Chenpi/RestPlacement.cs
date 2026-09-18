using System;
using System.Collections.Generic;

namespace Chenpi;

public sealed partial class PetEngine
{
    // Same logical coordinates as the 170px cat hit bounds and rendered props.
    // Expand each object by the resting cat footprint plus a small visible gap.
    private const double RestHalfWidth=85,RestGap=8;
    private (double X,double Radius)[] RestObstacles()=>new[]{
        (Nest.X,98+RestHalfWidth+RestGap),(FoodSpot.X,36+RestHalfWidth+RestGap),
        (WaterSpot.X,36+RestHalfWidth+RestGap),(LitterSpot.X,InteractionGeometry.LitterHalfWidth+RestHalfWidth+RestGap)};

    public bool IsClearRestSpot(double x)
    {
        if(!Grounded)return true;
        foreach(var item in RestObstacles())if(Math.Abs(x-item.X)<item.Radius-.01)return false;
        return x>=RestHalfWidth&&x<=Width-RestHalfWidth;
    }

    public Spot NearestRestSpot(Spot desired)
    {
        if(!Grounded)return desired;
        double lo=RestHalfWidth,hi=Math.Max(lo,Width-RestHalfWidth);
        double requested=Math.Clamp(desired.X,lo,hi);
        var obstacles=RestObstacles();
        var candidates=new List<double>{requested,lo,hi};
        foreach(var item in obstacles)
        {candidates.Add(Math.Clamp(item.X-item.Radius,lo,hi));candidates.Add(Math.Clamp(item.X+item.Radius,lo,hi));}
        double best=requested,distance=double.MaxValue;
        foreach(double x in candidates)
            if(IsClearRestSpot(x)&&Math.Abs(x-requested)<distance){best=x;distance=Math.Abs(x-requested);}
        if(distance==double.MaxValue)
        {
            // An overfilled/narrow desktop can have no feasible gap. Pick maximum
            // clearance deterministically, without moving furniture or oscillating.
            foreach(var a in obstacles)foreach(var b in obstacles)
                candidates.Add(Math.Clamp((a.X+a.Radius+b.X-b.Radius)/2,lo,hi));
            double clearance=double.MinValue;
            foreach(double x in candidates)
            {
                double score=double.MaxValue;
                foreach(var item in obstacles)score=Math.Min(score,Math.Abs(x-item.X)-item.Radius);
                if(score>clearance+.01||(Math.Abs(score-clearance)<.01&&Math.Abs(x-requested)<Math.Abs(best-requested)))
                {best=x;clearance=score;}
            }
        }
        return new(best,GroundY);
    }

    private bool LeaveOccupiedRestSpot()
    {
        if(!Grounded||IsClearRestSpot(State.X))return false;
        var free=NearestRestSpot(new(State.X,GroundY));
        if(Math.Abs(free.X-State.X)<2)return false;
        Go(free,"settle","到物品旁边的空位休息");return true;
    }
}
