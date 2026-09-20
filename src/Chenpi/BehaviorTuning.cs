using System;
using System.Collections.Generic;

namespace Chenpi;

public sealed partial class PetEngine
{
    public BehaviorSettings Settings {get;private set;}=new();
    public void ApplyBehaviorSettings(BehaviorSettings settings,bool restartCareClocks=false)
    {
        settings.Validate();Settings=settings.Copy();
        // Do not interrupt the current animation, consume inventory, cancel an
        // active care request, or mutate any stored furniture position.
        restReady=false;nextSeatedCall=Now+Settings.Get("sit.callCooldown");
        if(restartCareClocks)foreach(string kind in CareKinds)ScheduleNext(kind);
        Dirty=true;
    }
    private bool IsNight(int hour)
    {
        int start=(int)Settings.Get("night.start"),end=(int)Settings.Get("night.end");
        return start!=end&&(start<end?hour>=start&&hour<end:hour>=start||hour<end);
    }
    public string ActiveBehaviorNode=>Holding||Action is "drag" or "land"?"input":
        ToyOverlaps||Action.StartsWith("toy-")?"toy":
        State.CareRequest is not null?(State.Guiding?"guide":"request"):
        Action=="eat"?"food":Action=="drink"?"water":Action is "toilet" or "bury"?"litter":
        Action is "rest-HL" or "rest-HR"?"wall":
        Action.StartsWith("expr-")?"gesture":
        Action.StartsWith("rest-")?"poses":
        Action=="sleep"?(State.SleepingInNest?"nest":"poses"):
        Action is "pet" or "paw" or "roll" or "rub" or "wake"?"input":
        Action=="walk"?"movement":"rest";
    public IReadOnlyDictionary<string,double> CareSecondsRemaining=>new Dictionary<string,double>{
        ["food"]=Math.Max(0,State.FoodClock.NextDue-State.TotalSeconds),
        ["water"]=Math.Max(0,State.WaterClock.NextDue-State.TotalSeconds),
        ["litter"]=Math.Max(0,State.LitterClock.NextDue-State.TotalSeconds)};
}
