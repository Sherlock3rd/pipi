using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chenpi;

internal static class BehaviorUpdateAudit
{
    internal static void Run(string directory)
    {
        foreach(string scenario in new[]{"meals","toy-right","toy-left","request"})
        {
            var state=new PetState{X=scenario=="toy-left"?700:350,Y=752,Scale=1,TotalSeconds=1000,Food=100,Water=100,Litter=0,RestDuration=3600,
                FoodPosition=new(1100,752),WaterPosition=new(1320,752),NestPosition=new(2000,752),LitterPosition=new(1650,752),
                FoodClock=new(){NextDue=scenario=="meals"?1:99999},WaterClock=new(){NextDue=scenario=="meals"?2:99999},LitterClock=new(){NextDue=99999}};
            if(scenario=="request"){state.Food=0;state.FoodClock.NextDue=1;state.FoodClock.UnavailableSince=0;state.CareRequest="food";state.X=1100;}
            var scene=new Scene(new PetEngine(state,7));
            try{scene.ExportBehaviorUpdateAudit(Path.Combine(directory,scenario),scenario);}finally{scene.ReleaseFrames();}
        }
    }
}

internal sealed partial class Scene
{
    internal void ExportBehaviorUpdateAudit(string directory,string scenario)
    {
        Directory.CreateDirectory(directory);Engine.Layout(2200,800);
        bool toy=scenario.StartsWith("toy-"),left=scenario=="toy-left";
        playback.RestorePose(toy?(left?"SL":"SR"):"F");
        if(toy){wandHeld=true;Engine.SetToy(true,new(Engine.State.X+(left?-300:300),Engine.CatPlayCenter.Y));}
        var samples=new List<object>();var transitions=new List<string>();string previous="";int count=0;
        var visual=new DrawingVisual{Effect=softShadow};
        for(int tick=0;tick<24*65;tick++)
        {
            Engine.AdvanceNeeds(1d/24);Engine.Update(1d/24,12);
            if(toy&&tick==24*32)Engine.SetToy(true,new(Engine.State.X+(left?220:-220),Engine.CatPlayCenter.Y));
            var frame=playback.Sample(Engine.AligningForCare?"care-ready":Engine.Action,Engine.Now,Engine.FacingLeft)!.Value;
            bool changed=frame.Clip!=previous;if(changed){transitions.Add(frame.Clip);previous=frame.Clip;}
            using(var dc=visual.RenderOpen())RenderArtwork(dc);
            samples.Add(new{Engine.Now,Engine.Action,Engine.ActionRevision,Engine.FacingLeft,Engine.State.X,Engine.State.Food,Engine.State.Water,Clip=frame.Clip,Frame=frame.Index});
            if(changed||tick%48==0)
            {
                var bitmap=new RenderTargetBitmap(2200,800,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output=File.Create(Path.Combine(directory,$"{count++:000}-{Engine.Now:00.00}-{frame.Clip}.png"));png.Save(output);
            }
        }
        File.WriteAllText(Path.Combine(directory,"trace.json"),JsonSerializer.Serialize(new{Scenario=scenario,Transitions=transitions,Samples=samples},new JsonSerializerOptions{WriteIndented=true}));
    }
}
