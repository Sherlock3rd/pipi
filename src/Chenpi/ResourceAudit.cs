using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chenpi;
internal static class ResourceAudit
{
    internal static void Run(string sourceAssets,string output)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(sourceAssets,"pets/bluecat/manifest.json")));
        var paths=doc.RootElement.GetProperty("animations").EnumerateObject().SelectMany(p=>p.Value.EnumerateArray().Select(v=>v.GetString()!)).Distinct().ToArray();
        bool prepared=doc.RootElement.GetProperty("videoMattePrepared").GetBoolean();var elapsed=new List<double>();int checkedFrames=0;
        byte[] Pixels(BitmapSource b){var pixels=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(pixels,b.PixelWidth*4,0);return pixels;}
        foreach(string path in paths)
        {
            var original=new BitmapImage();original.BeginInit();original.CacheOption=BitmapCacheOption.OnLoad;original.UriSource=new Uri(Path.Combine(sourceAssets,"pets/bluecat",path));original.EndInit();original.Freeze();
            var expected=Scene.PrepareBitmap(original,true,out var expectedBounds,1,!prepared);
            long started=Stopwatch.GetTimestamp();
            var decoded=Scene.DecodeSource(Path.Combine(RuntimeAssets.Root,"pets/bluecat",path));
            var actual=Scene.PrepareBitmap(decoded,true,out var actualBounds,1,!prepared);elapsed.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if(expectedBounds!=actualBounds||expected.PixelWidth!=actual.PixelWidth||expected.PixelHeight!=actual.PixelHeight||!Pixels(expected).SequenceEqual(Pixels(actual)))throw new InvalidDataException("Prepared pixel mismatch: "+path);
            checkedFrames++;if(checkedFrames%100==0){GC.Collect();GC.WaitForPendingFinalizers();}
        }
        elapsed.Sort();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{CheckedFrames=checkedFrames,PixelMismatches=0,BoundsMismatches=0,P50DecodeMs=elapsed[elapsed.Count/2],P95DecodeMs=elapsed[(int)(elapsed.Count*.95)],MaxDecodeMs=elapsed[^1]},new JsonSerializerOptions{WriteIndented=true}));
    }
}
