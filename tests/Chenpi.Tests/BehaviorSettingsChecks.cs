using Chenpi;
using System.Text.Json;

public static class BehaviorSettingsChecks
{
    public static void Run(Action<bool,string> check,string manifestPath)
    {
        var defaults=new BehaviorSettings();defaults.Validate();
        check(BehaviorSettings.Catalog.Select(p=>p.Key).Distinct().Count()==BehaviorSettings.Catalog.Count,"behavior catalog has unique parameters and valid defaults");
        foreach(var invalid in new[]{new Dictionary<string,double>{{"food.min",80},{"food.max",20}},new(){{"toy.inner",600}},new(){{"sleep.delay",-1}},new(){{"wall.enabled",.5}},new(){{"walk.callEvery",1.5}},new(){{"unknown",3}}})
        {bool failed=false;try{new BehaviorSettings{Values=invalid}.Validate();}catch(InvalidDataException){failed=true;}check(failed,"invalid behavior configuration rejected: "+invalid.Keys.First());}
        var weights=new BehaviorSettings();foreach(var parameter in BehaviorSettings.Catalog.Where(p=>p.Group=="poses"))weights.Values[parameter.Key]=0;
        bool zeroRejected=false;try{weights.Validate();}catch(InvalidDataException){zeroRejected=true;}check(zeroRejected,"all-zero pose weights rejected without a hidden fallback");
        weights.Values["pose.M"]=1;weights.Validate();var rng=new Random(8);check(Enumerable.Range(0,200).All(_=>weights.Choose(rng,"pose.D","pose.A","pose.B","pose.X","pose.M")=="pose.M"),"zero weights never selected; a single positive weight selects masked sleep");
        string temp=Path.Combine(Path.GetTempPath(),"chenpi-behavior-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        var store=new BehaviorSettingsStore(temp);var settings=new BehaviorSettings{Values=new(){{"sleep.delay",12},{"food.min",2},{"food.max",2},{"hover.delay",1}}};store.Save(settings);check(store.Load().Get("sleep.delay")==12,"behavior settings persist through a fresh store instance");
        settings.Values["sleep.delay"]=13;store.Save(settings);File.WriteAllText(store.PathName,"broken");check(store.Load().Get("sleep.delay")==12&&store.Warning is not null,"corrupt behavior configuration recovers backup without deleting evidence");
        File.WriteAllText(store.PathName+".bak","broken too");check(store.Load().Get("sleep.delay")==30&&File.ReadAllText(store.PathName)=="broken","unreadable configurations preserve both files and use defaults");
        var state=new PetState{Food=80,Water=80,RestDuration=7200,RestElapsed=10,FoodClock=new(){NextDue=1000},WaterClock=new(){NextDue=1100},LitterClock=new(){NextDue=1200}};
        var cat=new PetEngine(state,seed:6,settings:settings){ExpressionsEnabled=true};check(state.RestDuration==7200,"long saved rest cycle survives engine restart with custom settings");
        double due=state.FoodClock.NextDue;cat.ApplyBehaviorSettings(weights);check(state.FoodClock.NextDue==due&&state.Food==80&&state.Water==80&&state.RestElapsed==10,"applying defaults preserves current care deadlines, inventory and rest progress");
        weights.Values["pose.M"]=20;check(cat.Settings.Get("pose.M")==1,"runtime settings are isolated from later editor draft mutations");
        cat.ApplyBehaviorSettings(settings,true);check(Math.Abs(cat.CareSecondsRemaining["food"]-120)<.001&&state.Food==80,"explicit reschedule uses configured period without consuming food");
        cat.ObservePointer(.6,true,new Spot());cat.ObservePointer(.5,true,new Spot());check(cat.Action=="rub","edited hover threshold affects actual input behavior");
        using var doc=JsonDocument.Parse(File.ReadAllText(manifestPath));var animations=doc.RootElement.GetProperty("animations");
        var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
        var tune=new BehaviorSettings{Values=new(){{"sleep.delay",2},{"pose.D",0},{"pose.A",0},{"pose.B",0},{"pose.X",0},{"pose.M",1}}};
        var e=new PetEngine(new PetState{Food=100,Water=100,Energy=100,RestDuration=3600,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}},seed:8,settings:tune){ExpressionsEnabled=true};e.VisualPoseReady=p.PreparePose;e.VisualActionDuration=p.ActionDuration;
        var clips=new HashSet<string>();for(int i=0;i<600;i++){e.Update(.05,12);var frame=p.Sample(e.Action,e.Now);if(frame is {} f)clips.Add(f.Clip);}
        check(e.RelaxedPose=="M"&&clips.Contains("video-84")&&e.State.Sleeping,"edited sleep time and pose weights drive the real animation graph to masked sleep");
        tune.Values["relax.gesture"]=100;tune.Values["relax.min"]=1;tune.Values["relax.max"]=1;tune.Values["gesture.80"]=0;tune.Values["gesture.90"]=1;tune.Values["gesture.96"]=0;e.ApplyBehaviorSettings(tune);e.PreviewRest("A");clips.Clear();
        for(int i=0;i<700;i++){e.Update(.05,12);var frame=p.Sample(e.Action,e.Now);if(frame is {} f)clips.Add(f.Clip);}
        check(clips.Contains("video-90")&&!clips.Contains("video-80")&&!clips.Contains("video-96"),"pose-specific gesture weights select only the configured supplied clip");
        tune.Values["click.window"]=2;e.ApplyBehaviorSettings(tune);e.Interact();for(int i=0;i<50;i++){e.Update(.05,12);p.Sample(e.Action,e.Now);}e.Interact();check(e.RelaxedClickCount==1,"edited fixed click window expires from the first click");
        p.RestorePose("SR");p.WalkingCallEvery=()=>0;clips.Clear();
        for(int leg=0;leg<4;leg++){p.RestorePose("SR");double x=100;for(double t=0;t<100;t+=.05){var move=p.TravelTo("walk",x,1500,t+leg*200,false)!.Value;x=move.X;clips.Add(p.Sample("walk",t+leg*200,false)!.Value.Clip);if(move.Complete)break;}}
        check(!clips.Contains("video-86"),"setting walking-call interval to zero disables insertion on long trips");
        Console.WriteLine("Behavior setting fixture files: "+temp);
    }
}
