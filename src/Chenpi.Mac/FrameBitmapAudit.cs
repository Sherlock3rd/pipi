using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Buffers;
using System.Text.Json;

namespace Chenpi;

internal static class FrameBitmapAudit
{
    // Independent, allocation-based reference for the pooled native decoder.
    private static unsafe FrameBitmap Reference(string path)
    {
        using var source=SkiaSharp.SKBitmap.Decode(path);
        using var raw=new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(source.Width,source.Height,SkiaSharp.SKColorType.Bgra8888,SkiaSharp.SKAlphaType.Unpremul));
        using var codec=SkiaSharp.SKCodec.Create(path);
        if(codec.GetPixels(raw.Info,raw.GetPixels())!=SkiaSharp.SKCodecResult.Success)throw new InvalidDataException(path);
        int w=raw.Width,h=raw.Height;var pixels=raw.Bytes;int minX=w,minY=h,maxX=0,maxY=0;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[(y*w+x)*4+3]>1)
        {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x+1);maxY=Math.Max(maxY,y+1);}
        if(maxX<=minX||maxY<=minY){minX=minY=0;maxX=maxY=1;}
        minX=Math.Max(0,minX-2);minY=Math.Max(0,minY-2);maxX=Math.Min(w,maxX+2);maxY=Math.Min(h,maxY+2);
        int cw=maxX-minX,ch=maxY-minY;var compact=new byte[cw*ch*4];
        for(int y=0;y<ch;y++)Buffer.BlockCopy(pixels,((minY+y)*w+minX)*4,compact,y*cw*4,cw*4);
        fixed(byte* p=compact)return new(new Bitmap(PixelFormat.Bgra8888,AlphaFormat.Unpremul,(IntPtr)p,new PixelSize(cw,ch),new Vector(96,96),cw*4),new Rect(minX/(double)w,minY/(double)h,cw/(double)w,ch/(double)h),null);
    }
    private static unsafe byte[] Pixels(FrameBitmap frame)
    {
        var pixels=new byte[frame.PixelWidth*frame.PixelHeight*4];
        fixed(byte* p=pixels)frame.Image.CopyPixels(new PixelRect(0,0,frame.PixelWidth,frame.PixelHeight),(IntPtr)p,pixels.Length,frame.PixelWidth*4);
        return pixels;
    }
    internal static void Run(string sourceAssets,string output)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(sourceAssets,"pets/bluecat/manifest.json")));
        if(!doc.RootElement.GetProperty("videoMattePrepared").GetBoolean())throw new InvalidDataException("Audit expects prepared video assets");
        var paths=doc.RootElement.GetProperty("animations").EnumerateObject().Where(p=>p.Name.StartsWith("video-"))
            .SelectMany(p=>{var f=p.Value.EnumerateArray().Select(x=>x.GetString()!).ToArray();return new[]{f[0],f[f.Length/2],f[^1]};}).Distinct().ToArray();
        foreach(var path in paths)
        {
            using var actual=FrameBitmap.Load(Path.Combine(RuntimeAssets.Root,"pets/bluecat",path),true,false);
            using var expected=Reference(Path.Combine(sourceAssets,"pets/bluecat",path));
            // Re-rent the same size bucket and overwrite it with a sentinel:
            // decoding identical data again would miss an aliased-buffer bug.
            var reused=ArrayPool<byte>.Shared.Rent(actual.PixelWidth*actual.PixelHeight*4);
            try
            {
                reused.AsSpan().Fill(0xA5);
                if(actual.Extent!=expected.Extent||actual.PixelWidth!=expected.PixelWidth||actual.PixelHeight!=expected.PixelHeight||!Pixels(actual).SequenceEqual(Pixels(expected)))throw new InvalidDataException("Native decoded pixel mismatch: "+path);
            }
            finally{ArrayPool<byte>.Shared.Return(reused);}
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{CheckedFrames=paths.Length,PixelMismatches=0,BoundsMismatches=0,PooledBufferLifetime="verified after reuse"}));
    }
}
