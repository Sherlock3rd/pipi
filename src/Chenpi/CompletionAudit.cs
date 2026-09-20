using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chenpi;

internal sealed partial class Scene
{
    internal void ExportCompletionAudit(string output,bool resume=false)
    {
        Directory.CreateDirectory(output);
        string audio=Path.Combine(AppContext.BaseDirectory,"assets","audio","cat");
        var catalog=VoiceCatalog.Parse(File.ReadAllText(Path.Combine(audio,"manifest.json")));
        using var player=new CatVoicePlayer(audio);player.SetVolume(.25);
        if(player.Warning is string error)throw new InvalidDataException(error);
        var routes=new List<object>();
        foreach(bool right in resume?Array.Empty<bool>():new[]{false,true})
        {
            Engine.Layout(3000,800);Engine.MoveObject("nest",new(right?2700:250,752));
            Engine.MoveObject("food",new(2100,752));Engine.MoveObject("water",new(2300,752));Engine.MoveObject("litter",new(2500,752));
            Engine.State.Food=80;Engine.State.Water=60;Engine.State.Litter=20;
            Engine.State.FoodClock.NextDue=Engine.State.WaterClock.NextDue=Engine.State.LitterClock.NextDue=999999;
            var cues=new VoiceCues(catalog,1);var voices=new List<object>();var timeline=new List<object>();var clips=new List<string>();
            string folder=Path.Combine(output,right?"right":"left");Directory.CreateDirectory(folder);
            if(!Engine.BeginStartup(preview:true))throw new InvalidDataException("Startup did not begin");
            int frameIndex=0;
            void Presented(SpriteFrame? f)
            {
                if(f is not SpriteFrame frame)return;
                var decision=cues.Observe(frame.Clip,frame.Index,Engine.ActionRevision,true);
                if(decision.Start is VoiceSound sound)voices.Add(new{Time=frameIndex/24d,frame.Clip,Frame=frame.Index,sound.File});
                player.Present(frame,Engine.ActionRevision,false);
            }
            FramePresented+=Presented;
            while(Engine.StartupActive&&frameIndex<24*240)
            {
                Engine.Support.Update(Engine);
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                {
                    dc.DrawRectangle(Brush("#E6E8DF"),null,new Rect(0,0,1500,440));dc.PushTransform(new ScaleTransform(.5,.5));
                    nestOcclusion=Engine.Support.Kind=="nest"&&InteractionGeometry.InsideNestSeat(Engine.VisualPosition.X,Engine.Nest.X);
                    DrawNest(dc,false);DrawLitter(dc);
                    DrawCat(dc,Engine.VisualPosition.X,Engine.VisualPosition.Y,Engine.Action,Engine.ActionTime,Engine.FacingLeft);
                    if(nestOcclusion)DrawNest(dc,true);
                    DrawBowl(dc,Engine.FoodSpot,Engine.State.Food,false,Engine.Now);DrawBowl(dc,Engine.WaterSpot,Engine.State.Water,true,Engine.Now);DrawWand(dc);dc.Pop();
                }
                if(clips.Count==0||clips[^1]!=DisplayedClip)clips.Add(DisplayedClip);
                // Export one full route as a review movie; the other route gets
                // native scene samples and the same per-frame timing trace.
                if(!right||frameIndex%24==0)SaveAuditImage(visual,Path.Combine(folder,$"{frameIndex:00000}.png"),1500,440);
                timeline.Add(new{Index=frameIndex,Time=frameIndex/24d,Clip=DisplayedClip,Frame=DisplayedFrame,Engine.Action,Engine.StartupStage,X=Engine.VisualPosition.X,Y=Engine.VisualPosition.Y,Support=DisplayedSupportHeight});
                frameIndex++;Engine.Update(1d/24,12);
            }
            FramePresented-=Presented;
            if(Engine.StartupActive||Engine.Action!="idle")throw new InvalidDataException("Startup stuck");
            int expected=catalog.Bindings.Count(b=>b.Clip=="video-117");
            if(voices.Count!=expected)throw new InvalidDataException("Mouth event count mismatch");
            File.WriteAllText(Path.Combine(folder,"timeline.json"),JsonSerializer.Serialize(timeline));
            File.WriteAllText(Path.Combine(folder,"voices.json"),JsonSerializer.Serialize(voices));
            routes.Add(new{NestRight=right,Frames=frameIndex,Seconds=frameIndex/24d,Clips=clips,Voices=voices,Engine.State.Food,Engine.State.Water,Engine.State.Litter,FinalX=Engine.State.X});
        }
        if(!resume)File.WriteAllText(Path.Combine(output,"verification.json"),JsonSerializer.Serialize(new{NativeFrameEvents=true,HardwareAudioLatencyMeasured=false,Routes=routes},new JsonSerializerOptions{WriteIndented=true}));
        Engine.Layout(3000,800);Engine.MoveObject("nest",new(2700,752));
        Engine.MoveObject("food",new(2100,752));Engine.MoveObject("water",new(2300,752));Engine.MoveObject("litter",new(2500,752));
        Engine.State.FoodClock.NextDue=Engine.State.WaterClock.NextDue=Engine.State.LitterClock.NextDue=999999;
        var carries=new List<object>();
        foreach(string pose in new[]{"D","A","B","X","M"})
        {
            Engine.State.X=1500;Engine.PreviewRest(pose);playback.RestorePose(pose);Engine.BeginDrag();
            var samples=new List<object>();string folder=Path.Combine(output,"carry-"+pose);Directory.CreateDirectory(folder);
            for(int i=0;i<24*24;i++)
            {
                if(i==240)Engine.Drop(false);
                Engine.Support.Update(Engine);
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                {
                    dc.DrawRectangle(Brush("#E6E8DF"),null,new Rect(0,0,500,460));
                    DrawCat(dc,250,420,Engine.Action,Engine.ActionTime,false);
                }
                if(i%24==0)SaveAuditImage(visual,Path.Combine(folder,$"{i:00000}.png"),500,460);
                if(!double.IsFinite(DisplayedSupportHeight))throw new InvalidDataException("Nonfinite support at "+DisplayedClip+":"+DisplayedFrame);
                samples.Add(new{Index=i,Clip=DisplayedClip,Frame=DisplayedFrame,Lift=DisplayedSupportHeight,Engine.Action});
                Engine.Update(1d/24,12);
                if(i>240&&Engine.Action!="land")break;
            }
            if(Engine.Action!="rest-"+pose)throw new InvalidDataException("Carry did not return to "+pose);
            carries.Add(new{Pose=pose,Samples=samples});
        }
        File.WriteAllText(Path.Combine(output,"carries.json"),JsonSerializer.Serialize(carries));
        ExportAnimationAudit(Path.Combine(output,"frames"),string.Join(',',Enumerable.Range(99,41).Concat(new[]{1,6,8,9,10,11,12,13,15,16,19,20,21,22,24,66,67,71,75,84}).Select(n=>n.ToString("00"))));
        ExportNestOcclusionAudit(Path.Combine(output,"nest"));
        File.WriteAllText(Path.Combine(output,"complete.txt"),"Native startup, carry, frame export and nest audit completed.");
    }
    private static void SaveAuditImage(DrawingVisual visual,string file,int width,int height)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);png.Save(stream);
    }
}
