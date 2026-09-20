using Chenpi;
using System.Text.Json;

public static class VoiceChecks
{
    public static void Run(Action<bool,string> check,string manifestPath)
    {
        string directory=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!,"../../audio/cat"));
        var catalog=VoiceCatalog.Parse(File.ReadAllText(Path.Combine(directory,"manifest.json")));
        using var doc=JsonDocument.Parse(File.ReadAllText(manifestPath));var animations=doc.RootElement.GetProperty("animations");
        check(catalog.Bindings.Length==9&&catalog.Bindings.SelectMany(b=>b.Sounds).Select(s=>s.Source).Distinct().Count()==7,"all seven supplied voices mapped to nine reviewed mouth windows");
        foreach(var binding in catalog.Bindings)
        {
            check(binding.ClosedFrame<animations.GetProperty(binding.Clip).GetArrayLength(),binding.Clip+" mouth window lies inside authored clip");
            foreach(var sound in binding.Sounds)
            {
                byte[] wav=File.ReadAllBytes(Path.Combine(directory,sound.File));
                check(System.Text.Encoding.ASCII.GetString(wav,0,4)=="RIFF"&&BitConverter.ToInt16(wav,20)==1&&BitConverter.ToInt16(wav,22)==1&&BitConverter.ToInt32(wav,24)==44100&&BitConverter.ToInt16(wav,34)==16&&System.Text.Encoding.ASCII.GetString(wav,36,4)=="data",sound.File+" is predecoded canonical PCM for immediate playback");
                check(Math.Abs((wav.Length-44)/88200d-sound.Seconds)<1e-6&&sound.Seconds<(binding.ClosedFrame-binding.OpenFrame)/binding.Fps,sound.File+" ends within the mouth-open interval");
            }
            var cues=new VoiceCues(catalog,3);var emitted=new List<int>();bool duplicate=false;
            for(int frame=0;frame<=binding.ClosedFrame+3;frame++)
            {
                if(cues.Observe(binding.Clip,frame,1,true).Start is not null)emitted.Add(frame);
                duplicate|=cues.Observe(binding.Clip,frame,1,true).Start is not null;
            }
            check(!duplicate,binding.Clip+" repeated paints never duplicate sound");
            check(emitted.SequenceEqual(new[]{binding.OpenFrame}),binding.Clip+" emits exactly at first reviewed open-mouth frame");
            check(cues.Observe(binding.Clip,0,1,true).Start is null&&cues.Observe(binding.Clip,binding.OpenFrame,1,true).Start is not null,binding.Clip+" next loop may speak once again");
            check(cues.Observe(binding.Clip,binding.OpenFrame+1,1,false).Stop&&cues.Observe(binding.Clip,binding.OpenFrame+1,1,true).Start is null,binding.Clip+" muting stops and unmuting does not replay mid-call");
            cues=new VoiceCues(catalog);cues.Observe(binding.Clip,binding.OpenFrame-1,1,true);
            check(cues.Observe(binding.Clip,binding.OpenFrame+2,1,true).Start is not null,binding.Clip+" small render skip crosses onset once");
            cues=new VoiceCues(catalog);check(cues.Observe(binding.Clip,binding.OpenFrame+3,1,true).Start is null,binding.Clip+" stale onset after seek or hidden interval is skipped");
            check(cues.Observe("video-84",0,2,true).Stop,binding.Clip+" interruption stops active sound");
        }
        var quiet=new VoiceCues(catalog);
        check(Enumerable.Range(0,97).All(i=>quiet.Observe("video-92",i,1,true).Start is null),"occluded mouth is explicitly pending, not assigned a guessed onset");
        // The graph may spend seconds waking/turning before the expression starts.
        var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
        var e=new PetEngine(new PetState{Food=100,Water=100,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}},7){ExpressionsEnabled=true};
        e.Layout(3000,800);e.State.X=400;e.VisualPoseReady=p.PreparePose;e.VisualTravel=p.TravelTo;e.VisualActionDuration=p.ActionDuration;
        e.ExecuteBehavior("expr-90");var sync=new VoiceCues(catalog);var observed=new List<(string Clip,int Index)>();
        for(int i=0;i<1800;i++){e.Update(1d/60,12);var f=p.Sample(e.Action,e.Now,e.FacingLeft)!.Value;if(sync.Observe(f.Clip,f.Index,e.ActionRevision,true).Start is not null)observed.Add((f.Clip,f.Index));}
        check(observed.SequenceEqual(new[]{("video-90",43)}),"real pose preparation remains silent and call follows displayed expression frame, not command time");
    }
}
