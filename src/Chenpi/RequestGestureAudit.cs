using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chenpi;

internal static class RequestGestureAudit
{
    internal static void Run(string directory,string kind,bool guided)
    {
        if(kind is not ("food" or "water"))throw new ArgumentException("Use food or water");
        var state=new PetState{X=700,Y=652,Scale=1,TotalSeconds=1000,Food=0,Water=0,RestDuration=3600,
            CareRequest=kind,Guiding=guided,NestPosition=new(2000,652),FoodPosition=new(700,652),WaterPosition=new(1200,652),LitterPosition=new(1750,652),
            FoodClock=new(){NextDue=kind=="food"?1:99999,UnavailableSince=0},WaterClock=new(){NextDue=kind=="water"?1:99999,UnavailableSince=0},LitterClock=new(){NextDue=99999}};
        new Scene(new PetEngine(state,7)).ExportRequestGestureAudit(directory,kind,guided);
    }
}

internal sealed partial class Scene
{
    internal void ExportRequestGestureAudit(string directory,string kind,bool guided)
    {
        Directory.CreateDirectory(directory);Directory.CreateDirectory(Path.Combine(directory,"sequence"));
        Engine.Layout(2400,700);Engine.State.X=(guided?Engine.GuideDestination(kind):Engine.RequestDestination(kind)).X;
        playback.RestorePose(guided?"SR":"F");bool refilled=false,recording=false;int count=0;double refillTime=-1;
        var framesOut=new List<object>();var trace=new List<object>();var transitions=new List<string>();
        void Save(string file,Action<DrawingContext> draw)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())draw(dc);
            var bitmap=new RenderTargetBitmap(768,640,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(directory,file));png.Save(stream);
        }
        void SceneCapture(string file,double root,SpriteFrame frame)
        {
            Save(file,dc=>{
                dc.DrawRectangle(Brush("#E6E8DF"),null,new Rect(0,0,768,640));
                dc.PushTransform(new ScaleTransform(2,2));dc.PushTransform(new TranslateTransform(192,256));
                DrawVideoFrame(dc,GetFrames(frame.Clip)![frame.Index],frame.Definition);
                DrawBowl(dc,new Spot(Engine.ObjectPosition(kind).X-root,0),kind=="food"?Engine.State.Food:Engine.State.Water,kind=="water",Engine.Now);
                dc.Pop();dc.Pop();
            });
        }
        for(int tick=0;tick<1200;tick++)
        {
            Engine.Update(1d/24,12);
            var frame=playback.Sample(Engine.Action,Engine.Now,Engine.FacingLeft)!.Value;
            bool complete=refilled&&Engine.Action is not ("care-finish" or "care-thanks");
            if(complete&&guided)break;
            recording|=Engine.Action==(guided?"guide-":"request-")+kind;
            if(!recording)continue;
            if(!refilled&&Engine.Action==(guided?"guide-":"request-")+kind&&Engine.ActionTime>=2)
            {
                SceneCapture("alignment-after.png",Engine.State.X,frame);
                if(!guided)SceneCapture("alignment-before.png",Engine.CareDestination(Engine.ObjectPosition(kind),kind=="food"?"eat":"drink").X,frame);
                SceneCapture("refill-before.png",Engine.State.X,frame);
                Engine.Refill(kind);refilled=true;refillTime=Engine.Now;
                var immediate=playback.Sample(Engine.Action,Engine.Now,Engine.FacingLeft)!.Value;
                if(frame.Clip!=immediate.Clip||frame.Index!=immediate.Index)throw new InvalidDataException("Refill changed the current sprite");
                SceneCapture("refill-immediate.png",Engine.State.X,immediate);
            }
            string file=$"sequence/{count++:0000}.png";
            Save(file,dc=>{dc.PushTransform(new ScaleTransform(2,2));dc.PushTransform(new TranslateTransform(192,256));DrawVideoFrame(dc,GetFrames(frame.Clip)![frame.Index],frame.Definition);dc.Pop();dc.Pop();});
            framesOut.Add(new{Clip="request-refill",Index=count-1,File=file,Definition=frame.Definition with {Loop=false}});
            trace.Add(new{Engine.Now,Engine.Action,SourceClip=frame.Clip,SourceFrame=frame.Index,Engine.State.X,Engine.State.Y,Engine.State.Food,Engine.State.Water,Refilled=refilled});
            if(transitions.Count==0||transitions[^1]!=frame.Clip){transitions.Add(frame.Clip);SceneCapture($"transition-{transitions.Count:00}-{frame.Clip}.png",Engine.State.X,frame);}
            if(complete)break;
        }
        if(!refilled||Engine.Action=="care-finish")throw new InvalidDataException("Request did not finish");
        File.WriteAllText(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(new{PixelsPerUnit=2,RootX=192,RootY=256,Frames=framesOut}));
        File.WriteAllText(Path.Combine(directory,"trace.json"),JsonSerializer.Serialize(new{Kind=kind,Guided=guided,RefillTime=refillTime,Transitions=transitions,Samples=trace}));
    }
}
