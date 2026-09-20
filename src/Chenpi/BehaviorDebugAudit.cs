using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chenpi;

internal sealed partial class Scene
{
    internal void ExportBehaviorDebugAudit(string output)
    {
        Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"sequence"));
        Engine.Layout(3000,800);Engine.State.X=400;
        Engine.MoveObject("nest",new(1800,752));Engine.MoveObject("food",new(1100,752));
        Engine.MoveObject("water",new(1350,752));Engine.MoveObject("litter",new(1550,752));
        Engine.State.FoodClock.NextDue=Engine.State.WaterClock.NextDue=Engine.State.LitterClock.NextDue=999999;
        var hitChecks=new Dictionary<string,bool>();
        hitChecks["nest"]=OpensSettingsAt(new(Engine.Nest.X,Engine.Nest.Y-80));
        foreach(var (name,point) in new[]{("cat",new Point(400,700)),("food",new Point(1100,740)),("water",new Point(1350,740)),("litter",new Point(1550,720)),("wand",new Point(Engine.WandHome.X,Engine.WandHome.Y)),("background",new Point(500,300))})
            hitChecks[name]=!OpensSettingsAt(point);
        foreach(var result in hitChecks)if(!result.Value)throw new InvalidDataException("Settings hit test failed: "+result.Key);
        var exported=new List<object>();var routes=new List<object>();int index=0;
        foreach(bool left in new[]{true,false})
        {
            Engine.ExecuteBehavior(left?"wall-left":"wall-right");
            var transitions=new List<string>();bool entered=false,exited=false;double maxStep=0,prior=Engine.State.X;
            for(int tick=0;tick<15000;tick++)
            {
                Engine.Update(1d/24,12);var frame=playback.Sample(Engine.Action,Engine.Now,Engine.FacingLeft)!.Value;
                maxStep=Math.Max(maxStep,Math.Abs(Engine.State.X-prior));prior=Engine.State.X;
                bool wall=int.TryParse(frame.Clip.Replace("video-",""),out int id)&&id>=52&&id<=57;
                if(wall)entered=true;
                if(entered)
                {
                    if(transitions.Count==0||transitions[^1]!=frame.Clip)transitions.Add(frame.Clip);
                    string file=$"sequence/{index++:0000}.png";var visual=new DrawingVisual();
                    using(var dc=visual.RenderOpen()){dc.PushTransform(new ScaleTransform(2,2));dc.PushTransform(new TranslateTransform(192,256));DrawVideoFrame(dc,GetFrames(frame.Clip)![frame.Index],frame.Definition);dc.Pop();dc.Pop();}
                    var bitmap=new RenderTargetBitmap(768,640,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,file)))png.Save(stream);
                    exported.Add(new{Clip=left?"wall-left":"wall-right",Index=index-1,File=file,SourceClip=frame.Clip,SourceFrame=frame.Index,RootX=Engine.State.X,RootY=Engine.State.Y,Definition=frame.Definition with {Loop=false}});
                    if(!wall){exited=true;break;}
                }
            }
            if(!exited)throw new InvalidDataException("Wall action did not complete");
            routes.Add(new{Left=left,MaxRootStep=maxStep,Transitions=transitions});
            Engine.BeginDrag();Engine.DragTo(new(400,752));Engine.Drop(false);
        }
        File.WriteAllText(Path.Combine(output,"frames.json"),JsonSerializer.Serialize(new{PixelsPerUnit=2,RootX=192,RootY=256,Frames=exported}));
        File.WriteAllText(Path.Combine(output,"verification.json"),JsonSerializer.Serialize(new{SettingsHitChecks=hitChecks,Routes=routes,FrameCount=exported.Count},new JsonSerializerOptions{WriteIndented=true}));
    }
}
