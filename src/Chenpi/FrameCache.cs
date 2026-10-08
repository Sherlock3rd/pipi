using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Chenpi;

// A byte budget, not a clip count: 640px clips and short loops cost very
// different amounts. Keys are source paths, so repeated frames share storage.
public sealed class FrameCache<T> : IDisposable where T : class
{
    private sealed record Entry(T Value,long Bytes,LinkedListNode<string> Node);
    private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    private readonly LinkedList<string> lru=new();
    private readonly object gate=new();
    private readonly Func<string,(T Value,long Bytes)> load;
    private readonly Action<T>? release;
    private readonly long budget;
    private Task worker=Task.CompletedTask;
    private string[] wanted=Array.Empty<string>();
    private bool disposed;
    public long Bytes {get;private set;}
    public long Misses {get;private set;}
    public long Hits {get;private set;}
    public double MaxLoadMilliseconds {get;private set;}
    public FrameCache(long budget,Func<string,(T Value,long Bytes)> load,Action<T>? release=null)
    {if(budget<=0)throw new ArgumentOutOfRangeException(nameof(budget));this.budget=budget;this.load=load;this.release=release;}
    public T Get(string key)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(entries.TryGetValue(key,out var entry)){Hits++;lru.Remove(entry.Node);lru.AddLast(entry.Node);return entry.Value;}
        }
        // IO and decompression must not hold the cache lock: a background
        // look-ahead must never block presentation of an already cached frame.
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        var result=load(key);
        lock(gate)
        {
            MaxLoadMilliseconds=Math.Max(MaxLoadMilliseconds,System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if(disposed){release?.Invoke(result.Value);throw new ObjectDisposedException(nameof(FrameCache<T>));}
            if(entries.TryGetValue(key,out var existing))
            {release?.Invoke(result.Value);Hits++;lru.Remove(existing.Node);lru.AddLast(existing.Node);return existing.Value;}
            Misses++;
            while(Bytes+result.Bytes>budget&&lru.First is {} oldest)
            {var old=entries[oldest.Value];entries.Remove(oldest.Value);lru.RemoveFirst();Bytes-=old.Bytes;release?.Invoke(old.Value);}
            entries.Add(key,new(result.Value,result.Bytes,lru.AddLast(key)));Bytes+=result.Bytes;
            return result.Value;
        }
    }
    // Only one short look-ahead queue exists. A change of action replaces stale
    // speculative IO instead of accumulating tasks for abandoned animations.
    public void Prefetch(string[] keys)
    {
        lock(gate)
        {
            if(disposed)return;wanted=keys;
            if(!worker.IsCompleted)return;
            worker=Task.Run(()=>
            {
                while(true)
                {
                    string? key=null;
                    lock(gate)
                    {
                        if(disposed)return;
                        foreach(var candidate in wanted)if(!entries.ContainsKey(candidate)){key=candidate;break;}
                        if(key is null){wanted=Array.Empty<string>();return;}
                    }
                    try{Get(key);}catch(Exception ex){System.Diagnostics.Trace.WriteLine("Frame prefetch: "+ex.Message);lock(gate)wanted=Array.Empty<string>();return;}
                }
            });
        }
    }
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;wanted=Array.Empty<string>();foreach(var e in entries.Values)release?.Invoke(e.Value);entries.Clear();lru.Clear();Bytes=0;}
    }
}
