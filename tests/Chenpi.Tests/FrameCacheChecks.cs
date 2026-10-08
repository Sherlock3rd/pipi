using Chenpi;

internal static class FrameCacheChecks
{
    private sealed record Frame(string Key);
    public static void Run(Action<bool,string> check)
    {
        var released=new List<string>();
        using var cache=new FrameCache<Frame>(30,key=>(new Frame(key),10),f=>released.Add(f.Key));
        var a=cache.Get("a");cache.Get("b");cache.Get("c");
        check(ReferenceEquals(a,cache.Get("a")),"cache reuses identical source frames");
        cache.Get("d");check(cache.Bytes==30&&released.SequenceEqual(new[]{"b"}),"byte budget evicts least recently used frame");
        for(int i=0;i<10000;i++)cache.Get("frame-"+i);
        check(cache.Bytes==30&&released.Count==10001,"long playback cannot retain all clips");
        cache.Dispose();check(cache.Bytes==0&&released.Count==10004,"host shutdown releases decoded frames");
        bool rejected=false;try{cache.Get("new");}catch(ObjectDisposedException){rejected=true;}check(rejected,"shutdown refuses late decoding");
        using var started=new ManualResetEventSlim();using var finish=new ManualResetEventSlim();
        using var asynchronous=new FrameCache<Frame>(100,key=>{if(key=="slow"){started.Set();finish.Wait();}return(new Frame(key),10);});
        asynchronous.Get("ready");asynchronous.Prefetch(new[]{"slow"});started.Wait();
        var read=Task.Run(()=>asynchronous.Get("ready"));bool responsive=read.Wait(1000);finish.Set();
        check(responsive,"background decompression never locks out cached presentation");
        SpinWait.SpinUntil(()=>asynchronous.Misses>=2,2000);
        using var sequenceCache=new FrameCache<Frame>(10000,key=>(new Frame(key),10));
        var sequence=new FrameSequence<Frame>(new[]{"first","second","first"},sequenceCache);
        check(sequence.Count==3&&ReferenceEquals(sequence[0],sequence[2]),"duplicate frame references retain timing while sharing memory");
        check(sequence[2].Key=="first"&&sequence[1].Key=="second"&&sequence[0].Key=="first","reverse playback preserves exact frame order");
    }
    public static void Playback(Action<bool,string> check,string manifestPath)
    {
        using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        var plain=new SpritePlayback();var prefetched=new SpritePlayback();
        int Count(string id)=>doc.RootElement.GetProperty("animations").TryGetProperty(id,out var v)?v.GetArrayLength():0;
        plain.Load(doc.RootElement,Count);prefetched.Load(doc.RootElement,Count);
        bool identical=true,valid=true;double now=0;
        foreach(string action in new[]{"idle","walk","guide-stop","eat","idle","sleep","wake","drag","land","rest-A","expr-69"})
        for(int i=0;i<7*60;i++,now+=1d/60)
        {
            var a=plain.Sample(action,now);var b=prefetched.Sample(action,now);
            identical&=a?.Clip==b?.Clip&&a?.Index==b?.Index&&a?.Definition.Width==b?.Definition.Width&&a?.Definition.AnchorY==b?.Definition.AnchorY;
            foreach(var f in prefetched.PeekFrames(action,now))valid&=f.Index>=0&&f.Index<Count(f.Clip);
        }
        check(identical,"read-ahead does not advance live transitions, care, sleep or drag playback");
        check(valid,"read-ahead remains within actual authored frame bounds");
        plain.Reset();prefetched.Reset();bool travelEqual=true;
        for(int i=0;i<1800;i++)
        {
            now=i/60d;var a=plain.TravelTo("walk",200,700,now,false);var b=prefetched.TravelTo("walk",200,700,now,false);
            travelEqual&=a==b;_ = prefetched.PeekFrames("walk",now).ToArray();
        }
        check(travelEqual,"read-ahead does not mutate the shared real-travel plan");
    }
}
