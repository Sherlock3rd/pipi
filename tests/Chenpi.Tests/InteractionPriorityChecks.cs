using Chenpi;
using System.Text.Json;

internal static class InteractionPriorityChecks
{
    public static void Run(Action<bool,string> check,string manifest)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(manifest));var animations=doc.RootElement.GetProperty("animations");
        (PetEngine E,SpritePlayback P) Create(string pose="F",int seed=7)
        {
            var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
            var s=new PetState{X=450,Food=100,Water=100,Litter=0,Energy=100,RestDuration=3600,
                NestPosition=new(2100,752),FoodPosition=new(1800,752),WaterPosition=new(1950,752),LitterPosition=new(900,752),
                FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}};
            var e=new PetEngine(s,seed){ExpressionsEnabled=true,CompletionEnabled=true,BodyInteractionsEnabled=true,BurialV6Enabled=true,
                VisualInteractionPose=()=>p.HitPose,VisualPoseReady=p.PreparePose,VisualActionDuration=p.ActionDuration,
                VisualTravel=p.TravelTo,VisualCareReady=p.PrepareCare,VisualBurialWindow=p.BurialWindow,VisualConsumptionWindow=p.ConsumptionWindow};
            e.Layout(2400,800);var settings=e.Settings.Copy();settings.Values["sleep.delay"]=300;settings.Values["move.chance"]=0;
            settings.Values["relax.min"]=settings.Values["relax.max"]=300;settings.Values["stand.stretchChance"]=0;e.ApplyBehaviorSettings(settings);
            if(pose is "A" or "B" or "C" or "D" or "X" or "M")e.PreviewRest(pose);
            p.RestorePose(pose);p.Sample("rest-"+pose,0);return(e,p);
        }
        string Step(PetEngine e,SpritePlayback p)
        {e.Update(1d/24,12);e.Support.Update(e);return p.Sample(e.AligningForCare?"care-ready":e.Action,e.Now,e.FacingLeft)?.Clip??"";}
        List<string> Tick(PetEngine e,SpritePlayback p,double seconds)
        {var clips=new List<string>();for(int i=0;i<seconds*24;i++){var c=Step(e,p);if(clips.Count==0||clips[^1]!=c)clips.Add(c);}return clips;}
        foreach(string pose in new[]{"F","SR","SL","A","B","C","D","X","M"})
        foreach(var part in Enum.GetValues<CatBodyPart>())
        {
            var(e,p)=Create(pose);e.Interact(part);var clips=Tick(e,p,18);
            check(!clips.Contains("video-143"),pose+" first "+part+" click never forces belly scratching");
            if(pose is "F" or "SR" or "SL")check(!clips.Any(c=>c is "video-58" or "video-60" or "video-62" or "video-101" or "video-102"),pose+" click stays off the lying path: "+part);
            if(pose is "A" or "X")check(!clips.Contains("video-62")&&!clips.Any(c=>c is "video-140" or "video-141" or "video-142" or "video-144"),pose+" retains its original lying response: "+part);
            if(pose=="M")check(clips.Contains("video-85")&&!clips.Contains("video-70")&&!clips.Contains("video-62"),"masked click only releases paws: "+part);
        }
        foreach(string pose in new[]{"A","B","X"})
        {
            var(e,p)=Create(pose);e.Interact(CatBodyPart.Belly);var clips=Tick(e,p,5);
            e.Interact(CatBodyPart.Belly);clips.AddRange(Tick(e,p,7));
            check(clips.Contains(pose=="B"?"video-143":pose=="A"?"video-69":"video-77")&&!clips.Contains("video-73"),pose+" second click replaces rather than stacks responses");
            e.Interact(CatBodyPart.Belly);clips.AddRange(Tick(e,p,12));
            check(clips.Contains(pose=="B"?"video-74":pose=="A"?"video-70":"video-78")&&e.RelaxedClickCount==0,pose+" third click still uses its direct standing exit");
        }
        {
            var(e,p)=Create("B");e.Interact(CatBodyPart.Belly);Tick(e,p,.1);e.Interact(CatBodyPart.Belly);e.Interact(CatBodyPart.Belly);
            var clips=Tick(e,p,15);check(clips.Contains("video-74")&&!clips.Contains("video-143"),"rapid lying clicks merge to highest pending level");
            (e,p)=Create("B");e.Interact(CatBodyPart.Belly);Tick(e,p,15);e.Interact(CatBodyPart.Belly);
            clips=Tick(e,p,5);check(e.RelaxedClickCount==1&&!clips.Contains("video-143"),"15-second expiry does not reuse an old belly second click");
            (e,p)=Create();e.Interact(CatBodyPart.Head);Tick(e,p,.1);e.Interact(CatBodyPart.Belly);e.Interact(CatBodyPart.Tail);
            clips=Tick(e,p,20);check(!clips.Contains("video-143")&&!clips.Contains("video-145"),"busy standing/seated response does not enqueue more shows");
            e.BeginDrag();Tick(e,p,1);check(e.Action=="drag","drag still cancels body response");
        }
        foreach(string care in new[]{"eat","drink","toilet","bury"})
        {
            var(e,p)=Create();if(care=="bury"){e.State.Litter=20;e.Demo("toilet");}else e.Demo(care);
            for(int i=0;i<24*180&&e.Action!=care;i++)Step(e,p);
            check(e.Action==care,"fixture reaches critical action "+care);
            string action=e.Action;long revision=e.ActionRevision;e.Interact(CatBodyPart.Belly);
            check(e.Action==action&&e.ActionRevision==revision,"light click does not interrupt "+care);
            var clips=Tick(e,p,25);check(!clips.Contains("video-143"),"critical action does not defer belly show: "+care);
        }
        check(CatBodyHit.Resolve(.5,.65,"F")!=CatBodyPart.Belly&&CatBodyHit.Resolve(.5,.6,"SR")!=CatBodyPart.Belly&&CatBodyHit.Resolve(.5,.6,"A")!=CatBodyPart.Belly,"non-exposed torso pixels are not belly hits");
        check(CatBodyHit.Resolve(.5,.55,"B")==CatBodyPart.Belly&&CatBodyHit.Resolve(.2,.55,"B")!=CatBodyPart.Belly,"belly hit is limited to exposed central region");
        var defaults=new BehaviorSettings();check(defaults.Get("litter.exitJumpChance")==50,"burial exit defaults to 50 percent jump");
        var saved=defaults.Copy();saved.Values["litter.exitJumpChance"]=30;
        check(BehaviorSettingsStore.Parse(JsonSerializer.Serialize(saved)).Get("litter.exitJumpChance")==30,"burial exit probability persists without changing other settings");
        foreach(bool left in new[]{true,false})foreach(int chance in new[]{0,100})
        {
            var(e,p)=Create();if(left)e.MoveObject("food",new(1218,752));else {e.MoveObject("litter",new(350,752));e.MoveObject("water",new(100,752));}
            var settings=e.Settings.Copy();settings.Values["litter.exitJumpChance"]=chance;e.ApplyBehaviorSettings(settings);e.Demo("toilet");
            bool burySeen=false,finished=false,finalJump=false;var after=new List<string>();var jumpFrames=new HashSet<int>();double previous=e.State.X,maxStep=0;
            for(int i=0;i<24*200;i++)
            {
                string c=Step(e,p);if(e.Action=="bury")burySeen=true;
                if(burySeen&&e.Action!="bury"){if(after.Count==0||after[^1]!=c)after.Add(c);}
                if(e.BurialStage=="final-jump")
                {
                    finalJump=true;maxStep=Math.Max(maxStep,Math.Abs(e.State.X-previous));
                    if(c is "video-147" or "video-148")jumpFrames.Add(p.Sample(e.Action,e.Now,e.FacingLeft)!.Value.Index);
                    if(jumpFrames.Count==10)e.Interact(CatBodyPart.Belly);
                }
                previous=e.State.X;
                if(burySeen&&e.LastBurialExit.Length>0&&e.BurialStage.Length==0&&e.Action.StartsWith("rest-")){finished=true;break;}
            }
            check(finished&&e.State.Litter==20&&e.State.LitterCover[0]==1,"complete burial preserved after exit: "+left+" "+chance);
            check(chance==100?finalJump&&jumpFrames.Count==97&&maxStep<30:!finalJump&&!after.Any(c=>c is "video-147" or "video-148"),"exit probability chooses complete jump or walk: "+left+" "+chance);
            check(!after.Contains("video-149")&&!after.Contains("video-150")&&!after.Contains("video-29")&&!after.Contains("video-143"),"final exit does not restart sniff/bury/click: "+left+" "+chance);
        }
        int jumps=0,walks=0;
        for(int seed=0;seed<100;seed++)
        {
            var(e,p)=Create(seed:seed);e.Demo("toilet");
            for(int i=0;i<24*180&&e.LastBurialExit.Length==0;i++)Step(e,p);
            if(e.LastBurialExit=="jump")jumps++;else if(e.LastBurialExit=="walk")walks++;
        }
        check(jumps>30&&jumps<70&&jumps+walks==100,"50 percent exit uses both branches across fixed seeds");
        Console.WriteLine($"Burial exit selections: jump={jumps}, walk={walks}");
        foreach(string interruption in new[]{"move-ready","move-air","clean-air","drag"})
        {
            var(e,p)=Create();var settings=e.Settings.Copy();settings.Values["litter.exitJumpChance"]=100;e.ApplyBehaviorSettings(settings);e.Demo("toilet");
            bool changed=false,done=false;var clips=new List<string>();
            for(int i=0;i<24*200;i++)
            {
                var c=Step(e,p);if(changed)clips.Add(c);
                if(!changed&&(interruption=="move-ready"?e.BurialStage=="final-exit-ready":e.BurialStage=="final-jump"&&e.ActionTime>2))
                {
                    changed=true;
                    if(interruption.StartsWith("move"))e.MoveObject("litter",new(1500,752));
                    else if(interruption=="clean-air")e.Refill("litter");else e.BeginDrag();
                }
                if(changed&&e.BurialStage.Length==0){done=true;break;}
            }
            check(changed&&done&&!clips.Contains("video-149")&&!clips.Contains("video-150"),"final exit remains finite without sniff replay after "+interruption);
            check(interruption!="drag"||e.Action=="drag","drag cancels final jump state");
        }
    }
}
