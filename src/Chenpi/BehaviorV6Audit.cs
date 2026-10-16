using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chenpi;

internal static class BehaviorV6Audit
{
    public static void Run(string directory,string? filter=null)
    {
        foreach(string scenario in new[]{"Head","Back","Belly","Paw","Tail","Back-left","Tail-left","burial-left","burial-right","toy-low-right","toy-low-left","toy-high-right","toy-high-left"})
        {
            if(filter is not null&&!filter.Split(',').Contains(scenario))continue;
            var s=new PetState{X=400,Scale=1,Food=100,Water=100,Litter=0,RestDuration=3600,NestPosition=new(2100,752),FoodPosition=new(1800,752),WaterPosition=new(1950,752),LitterPosition=new(900,752),FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}};
            var scene=new Scene(new PetEngine(s,7));
            try{scene.ExportBehaviorV6Audit(Path.Combine(directory,scenario),scenario);}finally{scene.ReleaseFrames();GC.Collect();GC.WaitForPendingFinalizers();}
        }
    }
}

internal sealed partial class Scene
{
    internal void ExportBehaviorV6Audit(string directory,string scenario)
    {
        Directory.CreateDirectory(directory);Engine.Layout(2400,800);
        bool burial=scenario.StartsWith("burial"),left=scenario.EndsWith("left"),toy=scenario.StartsWith("toy-");
        var visual=new DrawingVisual();var samples=new List<object>();var hits=new List<object>();string previous="";int count=0;
        void Render(){using var dc=visual.RenderOpen();RenderArtwork(dc);}
        void Save(string tag)
        {
            var bitmap=new RenderTargetBitmap(2400,800,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,$"{count++:000}-{tag}.png"));png.Save(file);
        }
        if(toy)
        {
            Engine.MoveObject("litter",new(1700,Engine.GroundY));
            Engine.State.X=800;Engine.HighToyGrabsEnabled=true; // Isolated diagnostics exercise both height branches.
            playback.RestorePose(left?"SL":"SR");wandHeld=true;
            Engine.SetToy(true,new(Engine.State.X+(left?-300:300),Engine.GroundY-(scenario.Contains("high")?130:65)));
        }
        else if(burial)
        {
            if(left)Engine.MoveObject("food",new(1218,Engine.GroundY));
            else {Engine.MoveObject("litter",new(350,Engine.GroundY));Engine.MoveObject("water",new(100,Engine.GroundY));}
            Engine.Demo("toilet");
        }
        else
        {
            var part=Enum.Parse<CatBodyPart>(scenario.Split('-')[0]);string pose=part==CatBodyPart.Belly?"B":part is CatBodyPart.Back or CatBodyPart.Tail?(left?"SL":"SR"):"F";
            Engine.PreviewRest(pose=="B"?"B":"D");playback.RestorePose(pose);
            // Render exactly the pose under the pointer before dispatching input.
            using(var dc=visual.RenderOpen()){dc.PushTransform(new ScaleTransform(Scale,Scale));DrawCat(dc,Engine.State.X,Engine.GroundY,"rest-"+pose,0,left);dc.Pop();}
            var bounds=CatRect;Point? chosen=null;int transparent=0,opaque=0;
            for(double y=bounds.Top;y<bounds.Bottom;y+=3)for(double x=bounds.Left;x<bounds.Right;x+=3)
            {
                var p=new Point(x,y);if(!CatContains(p)){transparent++;continue;}opaque++;
                if(BodyPartAt(p)==part)chosen??=p;
            }
            if(chosen is not Point hit)throw new InvalidOperationException("No visible hit region: "+scenario);
            hits.Add(new{Part=part.ToString(),hit.X,hit.Y,TransparentRejected=transparent,OpaqueAccepted=opaque});Save("hit");
            RememberBodyHit(hit);Engine.Interact(pressedBodyPart,pressedBodyLeft);
        }
        bool seen=false;
        for(int tick=0;tick<24*(burial?180:85);tick++)
        {
            Engine.Update(1d/24,12);Engine.Support.Update(Engine);
            var f=playback.Sample(Engine.AligningForCare?"care-ready":Engine.Action,Engine.Now,Engine.FacingLeft)!.Value;
            bool changed=f.Clip!=previous;previous=f.Clip;
            samples.Add(new{Engine.Now,Engine.Action,Engine.BurialStage,Engine.State.X,Support=Engine.Support.Height,Clip=f.Clip,Frame=f.Index,Cover=Engine.State.LitterCover[0],Target=Engine.LitterTarget});
            if(changed||tick%12==0){Render();Save($"{Engine.Now:000.00}-{f.Clip}-{f.Index:000}");}
            if(int.TryParse(f.Clip.Replace("video-",""),out int id)&&id>=140)seen=true;
            if(toy?seen&&f.Index==96&&f.Clip is "video-151" or "video-152" or "video-153" or "video-154":burial?Engine.State.LitterCover[0]==1:seen&&!Engine.Action.StartsWith("expr-")&&!Engine.Action.StartsWith("rest-")){Render();Save("complete");break;}
        }
        if(!seen)throw new InvalidOperationException("No new clip rendered: "+scenario);
        File.WriteAllText(Path.Combine(directory,"trace.json"),JsonSerializer.Serialize(new{scenario,Hits=hits,Samples=samples}));
    }
}
