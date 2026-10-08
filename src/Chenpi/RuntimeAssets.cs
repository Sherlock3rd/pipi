using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text.Json;

namespace Chenpi;

// Source checkouts keep PNGs. Release packs contain lossless WebP payloads;
// the original manifest paths remain stable, including repeated frame timing.
public static class RuntimeAssets
{
    private static readonly object gate=new();
    private static ZipArchive? pack;
    private static Dictionary<string,string>? index;
    private static bool initialized;
    public static string Root=>Path.Combine(AppContext.BaseDirectory,"assets");
    private static void Initialize()
    {
        if(initialized)return;
        string file=Path.Combine(Root,"frames.cpak");
        if(File.Exists(file))
        {
            pack=ZipFile.OpenRead(file);
            using var input=pack.GetEntry("index.json")!.Open();
            index=JsonSerializer.Deserialize<Dictionary<string,string>>(input)??throw new InvalidDataException("Empty frame pack");
        }
        initialized=true;
    }
    private static string Key(string path)=>Path.GetRelativePath(Root,path).Replace('\\','/');
    public static bool Exists(string path)
    {lock(gate){Initialize();return index?.ContainsKey(Key(path))==true||File.Exists(path);}}
    public static Stream Open(string path,out bool webp)
    {
        lock(gate)
        {
            Initialize();
            if(index is not null&&index.TryGetValue(Key(path),out var name))
            {
                using var source=pack!.GetEntry(name)!.Open();var copy=new MemoryStream();source.CopyTo(copy);copy.Position=0;webp=true;return copy;
            }
        }
        webp=false;return File.OpenRead(path);
    }
}

public sealed class WeakMetadata<TKey,TValue> where TKey:class
{
    private sealed class Box { public TValue Value;public Box(TValue value)=>Value=value; }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TKey,Box> entries=new();
    public TValue this[TKey key] {set {entries.AddOrUpdate(key,new(value));}}
    public bool TryGetValue(TKey key,out TValue value)
    {if(entries.TryGetValue(key,out var found)){value=found.Value;return true;}value=default!;return false;}
}
