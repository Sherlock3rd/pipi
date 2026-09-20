using System;
using System.Collections.Generic;
using System.Linq;

namespace Chenpi;

public sealed record MovementChoice(string Id, double X, double Weight, string Arrival);

public sealed partial class PetEngine
{
    private double movementDelay=-1;
    private bool movementConsidered;
    public string LastMovementChoice {get;private set;}="";

    // Ordinary destinations and wall destinations share this one weighted draw.
    // The wall weight is shared by the currently available sides, never doubled.
    public IReadOnlyList<MovementChoice> MovementChoices()
    {
        var choices=new List<MovementChoice>();
        foreach(var (id,fraction) in new[]{("left",.2),("center",.5),("right",.8)})
        {
            double x=NearestRestSpot(new(Width*fraction,GroundY)).X;
            double weight=Settings.Get("move."+id);
            if(weight>0&&IsClearRestSpot(x)&&Math.Abs(x-State.X)>40&&!choices.Any(c=>Math.Abs(c.X-x)<2))
                choices.Add(new("move-"+id,x,weight,"settle"));
        }
        if(Settings.Get("wall.enabled")>0&&Settings.Get("move.wall")>0)
        {
            var sides=new[]{true,false}.Where(left=>WallBlockReason(left).Length==0).ToArray();
            foreach(bool left in sides)choices.Add(new(left?"wall-left":"wall-right",WallGoal(left),Settings.Get("move.wall")/sides.Length,left?"rest-HL":"rest-HR"));
        }
        return choices;
    }
    private bool StartMovementChoice(string? requested=null)
    {
        var choices=MovementChoices().Where(c=>requested is null||c.Id==requested).ToArray();
        if(choices.Length==0)return false;
        double pick=random.NextDouble()*choices.Sum(c=>c.Weight);
        var chosen=choices[^1];
        foreach(var c in choices){pick-=c.Weight;if(pick<0){chosen=c;break;}}
        LastMovementChoice=chosen.Id;
        Go(new(chosen.X,GroundY),chosen.Arrival,"自主移动："+chosen.Id);
        return true;
    }
    private bool TryAutonomousMovement()
    {
        if(!ExpressionsEnabled||!Grounded||manualSequence||State.Sleeping||Action is not ("idle" or "sit")||State.StillSeconds>=RestDelay||State.RestElapsed>=State.RestDuration||State.Energy<24||IsNight(localHour)&&Now>nightRestUntil&&State.Energy<90)return false;
        if(movementDelay<0)movementDelay=Settings.Range(random,"move.delay");
        if(movementConsidered||State.StillSeconds<movementDelay)return false;
        movementConsidered=true;
        return random.NextDouble()*100<Settings.Get("move.chance")&&StartMovementChoice();
    }
    private void ResetMovementChoice(){movementDelay=-1;movementConsidered=false;}
}
