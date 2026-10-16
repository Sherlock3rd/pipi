using Chenpi;
using System.Text.Json;

internal static class BehaviorV6Checks
{
    public static void Run(Action<bool,string> check,string manifest)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(manifest));var animations=doc.RootElement.GetProperty("animations");
        (PetEngine,SpritePlayback) Create(bool left=false)
        {
            var pb=new SpritePlayback();pb.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
            var s=new PetState{X=left?1500:400,Litter=0,Food=100,Water=100,RestDuration=3600,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999},NestPosition=new(2200,752),FoodPosition=new(1900,752),WaterPosition=new(2050,752),LitterPosition=new(900,752)};
            var e=new PetEngine(s,7){ExpressionsEnabled=true,CompletionEnabled=true,BodyInteractionsEnabled=pb.HasBodyInteractions,BurialV6Enabled=pb.HasBurialV6,VisualPoseReady=pb.PreparePose,VisualActionDuration=pb.ActionDuration,VisualTravel=pb.TravelTo,VisualCareReady=pb.PrepareCare,VisualBurialWindow=pb.BurialWindow};
            e.Layout(2400,800);pb.RestorePose(left?"SL":"F");return(e,pb);
        }
        string Step(PetEngine e,SpritePlayback pb){e.Update(1d/24,12);e.Support.Update(e);return pb.Sample(e.AligningForCare?"care-ready":e.Action,e.Now,e.FacingLeft)?.Clip??"";}
        foreach(var part in Enum.GetValues<CatBodyPart>())
        {
            var(e,pb)=Create();e.Interact(part);var frames=new HashSet<int>();string expected="video-"+(part switch{CatBodyPart.Head=>140,CatBodyPart.Back=>141,CatBodyPart.Belly=>143,CatBodyPart.Paw=>144,_=>145});
            for(int i=0;i<24*80;i++){string clip=Step(e,pb);if(clip==expected)frames.Add(pb.Sample(e.Action,e.Now,e.FacingLeft)!.Value.Index);if(frames.Count>0&&e.Action!="expr-"+expected[6..])break;}
            check(frames.Count>=animations.GetProperty(expected).GetArrayLength()-1,"body click finishes complete native clip: "+part+" frames="+frames.Count);
        }
        {
            var(e,pb)=Create();e.Sleep();Step(e,pb);e.Interact(CatBodyPart.Paw);bool seenWake=false,seenPaw=false;
            for(int i=0;i<24*70;i++){string clip=Step(e,pb);seenWake|=clip is "video-21" or "video-18";seenPaw|=clip=="video-144";}
            check(seenWake&&seenPaw,"sleep click wakes through real graph before paw response");
            e.Interact(CatBodyPart.Belly);Step(e,pb);e.BeginDrag();for(int i=0;i<50;i++)Step(e,pb);
            check(e.Action=="drag","hold-drag cancels queued body reaction");
        }
        foreach(string mode in new[]{"left","right","move","move-sniff","move-ready","clean","clean-air"})
        {
            var(e,pb)=Create();if(mode=="right"){e.MoveObject("litter",new(350,e.GroundY));e.MoveObject("water",new(100,e.GroundY));}else e.MoveObject("food",new(1218,e.GroundY));
            e.Demo("toilet");var clips=new List<string>();bool changed=false,earlyCover=false,jumpEnded=false;double lastX=e.State.X,maxDelta=0,lastSupport=0,maxSupportDelta=0;
            for(int i=0;i<24*180;i++)
            {
                string clip=Step(e,pb);if(clips.Count==0||clips[^1]!=clip)clips.Add(clip);
                if(e.BurialStage.Length>0)earlyCover|=e.State.LitterCover.Any(x=>x!=0);
                if(e.BurialStage=="jump"){maxDelta=Math.Max(maxDelta,Math.Abs(e.State.X-lastX));maxSupportDelta=Math.Max(maxSupportDelta,Math.Abs(e.Support.Height-lastSupport));}else if(clips.Contains("video-147")||clips.Contains("video-148"))jumpEnded=true;
                lastX=e.State.X;lastSupport=e.Support.Height;
                if(!changed&&((mode=="move"&&e.BurialStage=="return")||(mode=="move-sniff"&&e.BurialStage=="sniff"&&e.ActionTime>2)||(mode=="move-ready"&&e.BurialStage=="exit-ready")||(mode=="clean"&&e.BurialStage=="sniff")||(mode=="clean-air"&&e.BurialStage=="jump"&&e.ActionTime>2)))
                {changed=true;if(mode.StartsWith("move"))e.MoveObject("litter",new(mode=="move-ready"?1200:1000,e.GroundY));else e.Refill("litter");}
                if(e.State.LitterCover[0]==1||changed&&e.State.Litter==0&&e.BurialStage.Length==0)break;
            }
            bool clean=mode.StartsWith("clean");int jump=clips.FindIndex(x=>x is "video-147" or "video-148"),sniff=clips.FindIndex(x=>x is "video-149" or "video-150"),bury=clips.IndexOf("video-29");
            check(jump>=0&&jumpEnded&&!earlyCover&&maxDelta<30&&maxSupportDelta<4,"burial jumps continuously before any cover: "+mode);
            check(clean?e.State.Litter==0&&bury<0:jump<sniff&&sniff<bury&&e.State.LitterCover[0]==1,"burial order / live clump cancellation: "+mode+" "+string.Join(',',clips));
            if(mode=="right")check(clips.Contains("video-147")&&clips.Contains("video-150"),"right exit uses authored right jump then left sniff");
            if(mode=="left")check(clips.Contains("video-148")&&clips.Contains("video-149"),"left exit uses authored left jump then right sniff");
        }
        check(CatBodyHit.Resolve(.83,.22,"SR")==CatBodyPart.Head&&CatBodyHit.Resolve(.17,.22,"SL")==CatBodyPart.Head,"body regions follow authored left/right orientation");
        check(CatBodyHit.Resolve(.5,.55,"B")==CatBodyPart.Belly&&CatBodyHit.Resolve(.5,.95,"F")==CatBodyPart.Paw,"lying belly and front paws remain independently clickable");
    }
}
