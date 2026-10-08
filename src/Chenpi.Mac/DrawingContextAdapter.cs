using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using System.Buffers;

namespace Chenpi;

// Keep scene geometry in one source file. Only API spelling differs from WPF.
internal sealed class DrawingContextAdapter : IDisposable
{
    private readonly DrawingContext dc;
    private readonly Stack<DrawingContext.PushedState> stack=new();
    public DrawingContextAdapter(DrawingContext dc)=>this.dc=dc;
    public void PushTransform(OriginTransform transform)=>stack.Push(dc.PushTransform(transform.Value));
    public void PushTransform(ITransform transform)=>stack.Push(dc.PushTransform(transform.Value));
    public void PushClip(Geometry geometry)=>stack.Push(dc.PushGeometryClip(geometry));
    public void Pop()=>stack.Pop().Dispose();
    public void DrawImage(FrameBitmap bitmap,Rect rect)=>dc.DrawImage(bitmap.Image,rect);
    public void DrawLine(IPen pen,Point a,Point b)=>dc.DrawLine(pen,a,b);
    public void DrawGeometry(IBrush? brush,IPen? pen,Geometry geometry)=>dc.DrawGeometry(brush,pen,geometry);
    public void DrawEllipse(IBrush? brush,IPen? pen,Point center,double x,double y)=>dc.DrawEllipse(brush,pen,center,x,y);
    public void DrawRoundedRectangle(IBrush? brush,IPen? pen,Rect rect,double x,double y)=>dc.DrawRectangle(brush,pen,rect,x,y);
    public void DrawText(FormattedText text,Point point)=>dc.DrawText(text,point);
    public void Dispose(){while(stack.Count>0)Pop();}
}
internal abstract class OriginTransform { public Matrix Value {get;protected set;} }
internal sealed class OriginScale : OriginTransform
{
    public OriginScale(double x,double y,double cx=0,double cy=0)=>Value=Matrix.CreateTranslation(-cx,-cy)*Matrix.CreateScale(x,y)*Matrix.CreateTranslation(cx,cy);
}
internal sealed class OriginRotate : OriginTransform
{
    public OriginRotate(double angle,double x=0,double y=0)=>Value=Matrix.CreateTranslation(-x,-y)*Matrix.CreateRotation(angle*Math.PI/180)*Matrix.CreateTranslation(x,y);
}
internal static class FrozenCompatibility
{
    // These objects are created once and never mutated after caching.
    public static void Freeze(this AvaloniaObject value) { }
}
internal readonly record struct Int32Rect(int X,int Y,int Width,int Height);
internal sealed class FrameBitmap : IDisposable
{
    public Bitmap Image {get;}
    public int PixelWidth=>Image.PixelSize.Width;
    public int PixelHeight=>Image.PixelSize.Height;
    public Rect Extent {get;}
    private readonly byte[]? hitPixels;
    internal FrameBitmap(Bitmap image,Rect extent,byte[]? hitPixels){Image=image;Extent=extent;this.hitPixels=hitPixels;}
    public long Bytes=>(long)PixelWidth*PixelHeight*4+(hitPixels?.Length??0);
    public static unsafe FrameBitmap Load(string path,bool crop,bool clean,int threshold=1,bool keepHitPixels=false,int decodeWidth=0)
    {
        using var source=RuntimeAssets.Open(path,out _);
        using var codec=SkiaSharp.SKCodec.Create(source);
        var info=new SkiaSharp.SKImageInfo(codec.Info.Width,codec.Info.Height,SkiaSharp.SKColorType.Bgra8888,SkiaSharp.SKAlphaType.Unpremul);
        using var raw=new SkiaSharp.SKBitmap(info);
        if(codec.GetPixels(info,raw.GetPixels())!=SkiaSharp.SKCodecResult.Success)throw new InvalidDataException(path);
        using var resized=decodeWidth>0&&decodeWidth!=info.Width?raw.Resize(new SkiaSharp.SKImageInfo(decodeWidth,(int)Math.Round(info.Height*decodeWidth/(double)info.Width),info.ColorType,info.AlphaType),SkiaSharp.SKFilterQuality.High):null;
        var selected=resized??raw;int w=selected.Width,h=selected.Height;
        // Inspect Skia's live decode buffer directly. Copying every full 640px
        // canvas into the managed large-object heap doubled transient storage.
        if(selected.RowBytes!=w*4)throw new InvalidDataException("Unexpected pixel stride");
        ReadOnlySpan<byte> pixels=new((void*)selected.GetPixels(),checked(w*h*4));
        if(clean)pixels=AlphaMatte.Clean(pixels.ToArray(),w,h);
        int minX=w,minY=h,maxX=0,maxY=0;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[(y*w+x)*4+3]>threshold)
        {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x+1);maxY=Math.Max(maxY,y+1);}
        if(maxX<=minX||maxY<=minY){minX=minY=0;maxX=maxY=1;}
        if(crop&&!clean){minX=Math.Max(0,minX-2);minY=Math.Max(0,minY-2);maxX=Math.Min(w,maxX+2);maxY=Math.Min(h,maxY+2);}
        Rect extent=new(minX/(double)w,minY/(double)h,(maxX-minX)/(double)w,(maxY-minY)/(double)h);
        if(!crop){minX=minY=0;maxX=w;maxY=h;extent=new(0,0,1,1);}
        int cw=maxX-minX,ch=maxY-minY;var compact=keepHitPixels?new byte[cw*ch*4]:ArrayPool<byte>.Shared.Rent(cw*ch*4);
        try
        {
            for(int y=0;y<ch;y++)pixels.Slice(((minY+y)*w+minX)*4,cw*4).CopyTo(compact.AsSpan(y*cw*4,cw*4));
            // Avalonia's constructor copies the buffer before returning.
            fixed(byte* address=compact)return new(new Bitmap(PixelFormat.Bgra8888,AlphaFormat.Unpremul,(IntPtr)address,new PixelSize(cw,ch),new Vector(96,96),cw*4),extent,keepHitPixels?compact:null);
        }
        finally{if(!keepHitPixels)ArrayPool<byte>.Shared.Return(compact);}
    }
    public void CopyPixels(Int32Rect rect,byte[] pixels,int stride,int offset)
    {
        if(hitPixels is null)throw new InvalidOperationException("Hit pixels not retained");
        for(int y=0;y<rect.Height;y++)Buffer.BlockCopy(hitPixels,((rect.Y+y)*PixelWidth+rect.X)*4,pixels,offset+y*stride,rect.Width*4);
    }
    public void Dispose()=>Image.Dispose();
}
