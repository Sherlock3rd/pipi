using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace Chenpi;

internal sealed class CatVoicePlayer : IDisposable
{
    private readonly VoiceCues cues;
    private readonly Dictionary<string,byte[]> originals=new();
    private readonly Dictionary<string,(MemoryStream Stream,SoundPlayer Player)> players=new();
    private SoundPlayer? active;
    private int volume=-1;
    private bool suppressed,disposed;
    internal string? Warning {get;private set;}
    internal CatVoicePlayer(string directory)
    {
        VoiceCatalog catalog;
        try
        {
            catalog=VoiceCatalog.Parse(File.ReadAllText(Path.Combine(directory,"manifest.json")));
            foreach(var binding in catalog.Bindings)foreach(var sound in binding.Sounds)
                originals.TryAdd(sound.File,File.ReadAllBytes(Path.Combine(directory,sound.File)));
        }
        catch(Exception e) when(e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {Warning=e.Message;catalog=new(){Version=1};originals.Clear();}
        cues=new(catalog);
    }
    internal void SetVolume(double value)
    {
        if(disposed)return;
        int level=(int)Math.Round(Math.Clamp(double.IsFinite(value)?value:0,0,1)*100);
        if(level==volume)return;
        Stop();foreach(var p in players.Values){p.Player.Dispose();p.Stream.Dispose();}players.Clear();volume=level;
        foreach(var (file,original) in originals)
        {
            byte[] wav=(byte[])original.Clone();
            // Prepared assets are canonical mono, 44.1kHz, 16-bit PCM WAV.
            for(int i=44;i+1<wav.Length;i+=2)
            {short sample=BitConverter.ToInt16(wav,i);short scaled=(short)(sample*level/100);wav[i]=(byte)(scaled&255);wav[i+1]=(byte)((scaled>>8)&255);}
            var stream=new MemoryStream(wav,false);var player=new SoundPlayer(stream);
            try{player.Load();players.Add(file,(stream,player));}
            catch(Exception e){Warning=e.Message;player.Dispose();stream.Dispose();}
        }
    }
    internal void Present(SpriteFrame? frame,long revision,bool audible)
    {
        if(disposed)return;
        bool allowed=audible&&volume>0&&!suppressed;suppressed=false;
        var decision=frame is SpriteFrame f?cues.Observe(f.Clip,f.Index,revision,allowed):cues.Observe("",0,revision,false);
        if(decision.Stop)Stop();
        if(decision.Start is VoiceSound sound&&players.TryGetValue(sound.File,out var loaded))
        {try{active=loaded.Player;active.Play();}catch(Exception e){Warning=e.Message;Stop();}}
    }
    internal void Silence(){suppressed=true;Stop();}
    private void Stop(){active?.Stop();active=null;}
    public void Dispose()
    {if(disposed)return;Stop();disposed=true;foreach(var p in players.Values){p.Player.Dispose();p.Stream.Dispose();}players.Clear();}
}
