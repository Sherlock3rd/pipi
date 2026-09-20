using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace Chenpi;

internal sealed partial class Scene
{
    internal void ExportVoiceAudit(string output)
    {
        Directory.CreateDirectory(output);
        string directory=Path.Combine(AppContext.BaseDirectory,"assets","audio","cat");
        var catalog=VoiceCatalog.Parse(File.ReadAllText(Path.Combine(directory,"manifest.json")));
        using var player=new CatVoicePlayer(directory);player.SetVolume(.25);
        if(player.Warning is string error)throw new InvalidDataException(error);
        Engine.Layout(3000,800);Engine.State.X=400;
        Engine.MoveObject("nest",new(2500,752));Engine.MoveObject("food",new(1800,752));
        Engine.MoveObject("water",new(2000,752));Engine.MoveObject("litter",new(2200,752));
        Engine.State.FoodClock.NextDue=Engine.State.WaterClock.NextDue=Engine.State.LitterClock.NextDue=999999;
        var settings=Engine.Settings.Copy();settings.Values["move.chance"]=0;Engine.ApplyBehaviorSettings(settings);
        var traces=new List<object>();
        foreach(int id in new[]{88,90,69})
        {
            var cues=new VoiceCues(catalog,1);var events=new List<(string Clip,int Frame)>();
            void Presented(SpriteFrame? frame)
            {
                if(frame is not SpriteFrame f)return;
                var d=cues.Observe(f.Clip,f.Index,Engine.ActionRevision,true);
                if(d.Start is not null)events.Add((f.Clip,f.Index));
                // Exercise loaded native player under the user's default silent
                // setting. Do not emit unsolicited audio during the audit.
                player.Present(frame,Engine.ActionRevision,false);
            }
            FramePresented+=Presented;double commandAt=Engine.Now;Engine.ExecuteBehavior("expr-"+id);
            for(int tick=0;tick<3600;tick++)
            {
                Engine.Update(1d/60,12);
                var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())DrawCat(dc,192,256,Engine.Action,Engine.ActionTime,Engine.FacingLeft);
                if(events.Count>0&&!Engine.DebugBehaviorActive)break;
            }
            FramePresented-=Presented;
            var marker=catalog.Bindings.Single(b=>b.Clip=="video-"+id);
            if(events.Count!=1||events[0]!=(marker.Clip,marker.OpenFrame))throw new InvalidDataException("Rendered mouth cue mismatch: "+id);
            traces.Add(new{Clip=marker.Clip,CommandAt=commandAt,Events=events.Select(e=>new{e.Clip,e.Frame}),UsesActualSceneDraw=true});
        }
        File.WriteAllText(Path.Combine(output,"verification.json"),JsonSerializer.Serialize(new{NativePcmPreload=true,AudibleHardwarePlaybackTested=false,UserSaveUntouched=true,Traces=traces},new JsonSerializerOptions{WriteIndented=true}));
    }
}
