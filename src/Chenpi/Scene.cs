using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;

namespace Chenpi;

internal sealed partial class Scene : FrameworkElement
{
    // Keep the alpha-based shadow off labels and controls, and apply it after
    // furniture occlusion so split rims cannot cast artificial dark seams.
    private sealed class ArtworkVisual : DrawingVisual
    {
        protected override HitTestResult? HitTestCore(PointHitTestParameters p)=>null;
    }
    private readonly DrawingVisual artwork=new ArtworkVisual(),overlay=new ArtworkVisual();
    private readonly DropShadowEffect softShadow=new(){Color=Colors.Black,Opacity=.38,BlurRadius=14,ShadowDepth=3,Direction=270,RenderingBias=RenderingBias.Performance};
    internal bool SoftShadowsEnabled {get=>artwork.Effect is not null;set=>artwork.Effect=value?softShadow:null;}
    protected override int VisualChildrenCount=>2;
    protected override Visual GetVisualChild(int index)=>index switch {0=>artwork,1=>overlay,_=>throw new ArgumentOutOfRangeException(nameof(index))};
    public PetEngine Engine {get;}
    public Action? OpenSettings;
    public Action? SaveNow;
    internal event Action<SpriteFrame?>? FramePresented;
    public bool IsDragging {get;private set;}
    private bool pressed;
    private double pressedAt;
    private Point pressedPoint;
    private Vector grabOffset;
    private string pressedObject="cat";
    private Spot originalPosition;
    private double shownFood,shownWater;
    private double previousVisualTime;
    private bool wandHeld;
    private Point pointer;
    private double lift;
    private readonly Cursor liftedCursor=Cursors.Hand;
    public const double HoldSeconds=.25;
    public int LiftTransitions {get;private set;}
    public double MaxLiftTransitionMs {get;private set;}
    public bool IsInteracting=>pressed||wandHeld;
    public double RenderMilliseconds {get;private set;}
    public int RenderCount {get;private set;}
    private static readonly Dictionary<string,SolidColorBrush> brushes=new();
    private static readonly Dictionary<string,System.Windows.Media.Geometry> shapes=new();
    private static readonly Dictionary<(string,double,string),FormattedText> labels=new();
    private readonly System.Diagnostics.Stopwatch watch=System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<string,IReadOnlyList<BitmapSource>> frames=new();
    private readonly Dictionary<string,List<string>> framePaths=new();
    private FrameCache<BitmapSource> frameCache=null!;
    private bool preparedMatte;
    private readonly System.Collections.Generic.HashSet<string> videoFramePaths=new();
    public long CachedFrameBytes=>frameCache.Bytes;
    public void ReleaseFrames()=>frameCache.Dispose();
    private readonly WeakMetadata<BitmapSource,Rect> frameBounds=new();
    private Rect? currentCatBounds;
    public const double BowlScale=.60;
    public bool DarkPreview {get;set;}
    internal static BitmapSource PrepareBitmap(BitmapSource original,bool crop,out Rect bounds,int boundsThreshold=8,bool cleanMatte=true)
    {
        var straight=new FormatConvertedBitmap(original,PixelFormats.Bgra32,null,0);
        int w=straight.PixelWidth,h=straight.PixelHeight;
        var pixels=new byte[w*h*4];straight.CopyPixels(pixels,w*4,0);
        if(cleanMatte)pixels=AlphaMatte.Clean(pixels,w,h);
        int minX=w,minY=h,maxX=0,maxY=0;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[(y*w+x)*4+3]>boundsThreshold)
        {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x+1);maxY=Math.Max(maxY,y+1);}
        // Keep a transparent sampling gutter around tightly cropped video frames.
        // Texture filtering must not extend a colored boundary texel outside the cat.
        if(crop&&!cleanMatte){minX=Math.Max(0,minX-2);minY=Math.Max(0,minY-2);maxX=Math.Min(w,maxX+2);maxY=Math.Min(h,maxY+2);}
        bounds=new Rect(minX/(double)w,minY/(double)h,(maxX-minX)/(double)w,(maxY-minY)/(double)h);
        BitmapSource result=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4);
        result=new FormatConvertedBitmap(result,PixelFormats.Pbgra32,null,0);result.Freeze();
        if(crop)
        {
            int cw=maxX-minX,ch=maxY-minY;
            var cut=new CroppedBitmap(result,new Int32Rect(minX,minY,cw,ch));
            var compact=new byte[cw*ch*4];cut.CopyPixels(compact,cw*4,0);
            result=BitmapSource.Create(cw,ch,96,96,PixelFormats.Pbgra32,null,compact,cw*4);result.Freeze();
        }
        return result;
    }
    private IReadOnlyList<BitmapSource>? GetFrames(string id)
    {
        if(frames.TryGetValue(id,out var cached))return cached;
        if(!framePaths.TryGetValue(id,out var paths))return null;
        var sequence=new FrameSequence<BitmapSource>(paths,frameCache);frames[id]=sequence;return sequence;
    }
    internal static BitmapSource DecodeSource(string path)
    {
        using var stream=RuntimeAssets.Open(path,out bool webp);
        if(!webp){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;}
        using var codec=SkiaSharp.SKCodec.Create(stream);
        var info=new SkiaSharp.SKImageInfo(codec.Info.Width,codec.Info.Height,SkiaSharp.SKColorType.Bgra8888,SkiaSharp.SKAlphaType.Unpremul);
        using var decoded=new SkiaSharp.SKBitmap(info);
        if(codec.GetPixels(info,decoded.GetPixels())!=SkiaSharp.SKCodecResult.Success)throw new InvalidDataException("Invalid lossless frame: "+path);
        var pixels=decoded.Bytes;
        var result=BitmapSource.Create(info.Width,info.Height,96,96,PixelFormats.Bgra32,null,pixels,info.Width*4);result.Freeze();return result;
    }
    private readonly AnimationVariants variants=new();
    private readonly SpritePlayback playback=new();
    private string lastSpriteClip="";
    private bool nestOcclusion;
    private double poseLift;
    public double DisplayedSupportHeight=>poseLift;
    private static BitmapSource? LoadProp(string name)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"assets","props",name+".png");
        if(!File.Exists(path))return null;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
        image.DecodePixelWidth=640;image.UriSource=new Uri(path);image.EndInit();image.Freeze();
        return PrepareBitmap(image,name is "nest" or "litter-tray" or "kibble",out _,name=="kibble"?128:8);
    }
    private static readonly BitmapSource? foodBowl=LoadProp("food-bowl-empty"),kibble=LoadProp("kibble"),waterCup=LoadProp("water-cup-empty");
    private static readonly BitmapSource? nestSprite=LoadProp("nest"),litterSprite=LoadProp("litter-tray");
    public string DisplayedClip {get;private set;}="";
    public int DisplayedFrame {get;private set;}
    public SpriteClip? DisplayedDefinition {get;private set;}
    public List<object> ClipTransitions {get;}=new();
    public bool PreviewRightWalk {get;set;}
    public bool PreviewSupplies {get;set;}
    private long spriteRevision=-1;
    private AnimationVariant? selectedVariant;
    private double fps=8;
    public double Scale=>Engine.State.Scale;
    public double WorldWidth=>ActualWidth/Scale;
    public double WorldHeight=>ActualHeight/Scale;
    public Scene(PetEngine engine,string? auditPreload=null,string? resumeFrames=null)
    {
        AddVisualChild(artwork);AddVisualChild(overlay);SoftShadowsEnabled=true;
        Engine=engine;shownFood=engine.State.Food;shownWater=engine.State.Water;Focusable=false;Cursor=Cursors.Arrow;
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        frameCache=new FrameCache<BitmapSource>(96L*1024*1024,file=>{
            var original=DecodeSource(file);
            bool video=videoFramePaths.Contains(file);
            var image=video?PrepareBitmap(original,true,out var bounds,1,!preparedMatte):original;
            if(video)frameBounds[image]=bounds;
            return (image,(long)image.PixelWidth*image.PixelHeight*4);
        });
        LoadSprites(auditPreload,resumeFrames);
        Engine.CanAdvanceMovement=null;
        Engine.VisualVelocity=playback.HorizontalVelocity;
        Engine.VisualActionDuration=playback.ActionDuration;
        Engine.VisualCareReady=playback.PrepareCare;
        Engine.VisualRequestFinishReady=playback.PrepareRequestFinish;
        Engine.VisualStandReady=playback.PrepareStand;
        playback.WalkingCallEvery=()=>(int)Engine.Settings.Get("walk.callEvery");
        Engine.VisualTravel=playback.TravelTo;
        Engine.VisualBurialWindow=playback.BurialWindow;
        Engine.VisualConsumptionWindow=playback.ConsumptionWindow;
        Engine.ExpressionsEnabled=playback.HasExpressions;
        Engine.VisualPoseReady=playback.PreparePose;
        Engine.VisualWallRestComplete=playback.WallRestComplete;
        Engine.CompletionEnabled=playback.HasCompletion;
        Engine.VisualRestorePose=playback.RestorePose;
        Engine.VisualDropPose=()=>playback.DropPose;
        // Prepare the first pickup pose before input, including decoded sprites and drawing caches.
        if(auditPreload is null){var warm=new DrawingGroup();using(var drawing=warm.Open())DrawCat(drawing,0,0,"drag",0,false);}
        playback.Reset();poseLift=0;
        Engine.RestoreRelaxedSleep();
        if(Engine.Action.StartsWith("rest-"))playback.RestorePose(Engine.RelaxedPose);
        SizeChanged+=(_,_)=>LayoutWorld();
        LostMouseCapture+=(_,_)=>{if(pressed||IsDragging||wandHeld)CancelDrag();};
    }
    private void LoadSprites(string? auditPreload=null,string? resumeFrames=null)
    {
        var path=Path.Combine(AppContext.BaseDirectory,"assets","pets","bluecat","manifest.json");
        if(!File.Exists(path))return;
        try
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(path));
            fps=Math.Clamp(doc.RootElement.GetProperty("fps").GetDouble(),1,30);
            var selected=auditPreload?.Split(',').Select(x=>x.StartsWith("video-")?x:"video-"+x).ToHashSet();
            string root=Path.GetFullPath(Path.GetDirectoryName(path)!)+Path.DirectorySeparatorChar;
            foreach(var property in doc.RootElement.GetProperty("animations").EnumerateObject())
            {
                if(selected is not null&&!selected.Contains(property.Name))continue;
                try
                {
                var images=new List<string>();
                foreach(var frame in property.Value.EnumerateArray())
                {
                    string full=Path.GetFullPath(Path.Combine(root,frame.GetString()!));
                    if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!full.EndsWith(".png",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid frame path");
                    if(!RuntimeAssets.Exists(full))throw new FileNotFoundException(full);images.Add(full);
                }
                if(images.Count>0){framePaths[property.Name]=images;if(property.Name.StartsWith("video-",StringComparison.Ordinal))foreach(string file in images)videoFramePaths.Add(file);}
                }
                catch(Exception ex){System.Diagnostics.Trace.WriteLine("Animation fallback: "+property.Name+": "+ex.Message);}
            }
            variants.Load(doc.RootElement,id=>framePaths.ContainsKey(id));
            playback.Load(doc.RootElement,id=>framePaths.TryGetValue(id,out var set)?set.Count:0);
            if(doc.RootElement.TryGetProperty("wallContactOffsets",out var wall))
            {Engine.WallRightOffset=wall.GetProperty("right").GetDouble();Engine.WallLeftOffset=wall.GetProperty("left").GetDouble();}
            if(doc.RootElement.TryGetProperty("relaxedHalfWidth",out var footprint))Engine.RelaxedHalfWidth=footprint.GetDouble();
            preparedMatte=doc.RootElement.TryGetProperty("videoMattePrepared",out var readyMatte)&&readyMatte.GetBoolean();
            // Warm only entry poses; frame sequences prefetch a small rolling window.
            foreach(string id in new[]{"video-01","video-20","video-32"})
                if(framePaths.TryGetValue(id,out var warm)&&warm.Count>0)frameCache.Get(warm[0]);

        }
        catch(Exception ex){System.Diagnostics.Trace.WriteLine("Sprite fallback: "+ex.Message);}
    }
    public void LayoutWorld()
    {
        if(WorldWidth<400||WorldHeight<250)return;
        Engine.Layout(WorldWidth,WorldHeight);
        InvalidateVisual();
    }
    public void InputTick(double elapsed)
    {
        pointer=World(Mouse.GetPosition(this));
        if(pressed&&!IsDragging&&watch.Elapsed.TotalSeconds-pressedAt>=Engine.Settings.Get("drag.hold"))
        {StartDrag();}
        double liftTarget=!Engine.Grounded&&pressed&&pressedObject=="cat"?(IsDragging?8:Math.Min(1,(watch.Elapsed.TotalSeconds-pressedAt)/Engine.Settings.Get("drag.hold"))*3):0;
        lift+=(liftTarget-lift)*(1-Math.Exp(-elapsed*24));
        if(IsDragging)MoveDragged(pointer);
        if(wandHeld)Engine.SetToy(true,new Spot(Math.Clamp(pointer.X,0,WorldWidth),Math.Clamp(pointer.Y,0,WorldHeight)));
        Engine.ObservePointer(elapsed,IsVisible&&IsMouseOver&&!pressed&&CatRect.Contains(pointer),new Spot(pointer.X,pointer.Y));
        double now=watch.Elapsed.TotalSeconds,step=Math.Min(.1,now-previousVisualTime)*95;previousVisualTime=now;
        shownFood+=Math.Clamp(Engine.State.Food-shownFood,-step,step);
        shownWater+=Math.Clamp(Engine.State.Water-shownWater,-step,step);
        InvalidateVisual();
    }
    private void StartDrag()
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        IsDragging=true;if(pressedObject=="cat")Engine.BeginDrag();Cursor=liftedCursor;
        MoveDragged(World(Mouse.GetPosition(this)));InvalidateVisual();
        LiftTransitions++;MaxLiftTransitionMs=Math.Max(MaxLiftTransitionMs,System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    public void CancelDrag()
    {
        wandHeld=false;Engine.SetToy(false,new Spot());
        pressed=false;Engine.Holding=false;
        if(IsDragging)
        {
            IsDragging=false;
            if(pressedObject=="cat"){Engine.State.X=originalPosition.X;Engine.State.Y=originalPosition.Y;Engine.Drop(false);}
            else Engine.MoveObject(pressedObject,originalPosition);
        }
        Cursor=Cursors.Arrow;if(IsMouseCaptured)ReleaseMouseCapture();SaveNow?.Invoke();
    }
    private Point World(Point p)=>new(p.X/Scale,p.Y/Scale);
    private Rect CatRect {get {
        if(currentCatBounds is Rect b){b.Offset(Engine.VisualPosition.X,Engine.VisualPosition.Y-lift-poseLift);b.Inflate(7,7);return b;}
        return new(Engine.State.X-85,Engine.State.Y-158,170,170);
    }}
    private Rect NestRect=>new(Engine.Nest.X-InteractionGeometry.NestHalfWidth,Engine.Nest.Y-InteractionGeometry.NestHeight,InteractionGeometry.NestWidth,InteractionGeometry.NestHeight);
    private Rect LitterRect=>new(Engine.LitterSpot.X-InteractionGeometry.LitterHalfWidth,Engine.LitterSpot.Y-InteractionGeometry.LitterHeight,InteractionGeometry.LitterHalfWidth*2,InteractionGeometry.LitterHeight);
    private Rect ObjectRect(Spot p,double w=92)=>p==Engine.FoodSpot||p==Engine.WaterSpot
        ?new(p.X-36,p.Y+InteractionGeometry.BowlBaseOffset(p==Engine.WaterSpot)-70,72,70):new(p.X-w/2,p.Y-62,w,62);
    private Rect WandRect=>new(Engine.WandHome.X-39,Engine.WandHome.Y-38,78,73);
    private bool AtNest=>Engine.CanDropInNest(new Spot(pointer.X,pointer.Y));
    protected override HitTestResult? HitTestCore(PointHitTestParameters p)
    {
        var at=World(p.HitPoint);
        return pressed||wandHeld||WandRect.Contains(at)||CatRect.Contains(at)||NestRect.Contains(at)||ObjectRect(Engine.FoodSpot).Contains(at)||ObjectRect(Engine.WaterSpot).Contains(at)||LitterRect.Contains(at)
            ?new PointHitTestResult(this,p.HitPoint):null;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p=World(e.GetPosition(this));
        if(wandHeld){ReturnWand();e.Handled=true;return;}
        if(WandRect.Contains(p))
        {wandHeld=true;Engine.SetToy(true,new Spot(p.X,p.Y));CaptureMouse();Cursor=Cursors.Cross;InvalidateVisual();e.Handled=true;return;}
        string? hit=FrontBowlContains(p,true)?"water":FrontBowlContains(p,false)?"food":CatRect.Contains(p)?"cat":LitterRect.Contains(p)?"litter":ObjectRect(Engine.WaterSpot).Contains(p)?"water":ObjectRect(Engine.FoodSpot).Contains(p)?"food":NestRect.Contains(p)?"nest":null;
        if(hit is null)return;
        pressedObject=hit;originalPosition=hit=="cat"?Engine.VisualPosition:Engine.ObjectPosition(hit);
        pressed=true;pressedAt=watch.Elapsed.TotalSeconds;pressedPoint=p;grabOffset=new Vector(p.X-originalPosition.X,p.Y-originalPosition.Y);Engine.Holding=true;CaptureMouse();e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p=World(e.GetPosition(this));
        if(pressed&&!IsDragging&&((p-pressedPoint).Length>=6||watch.Elapsed.TotalSeconds-pressedAt>=Engine.Settings.Get("drag.hold")))StartDrag();
        MoveDragged(p);
        if(IsDragging||wandHeld)InvalidateVisual();
    }
    private void MoveDragged(Point p)
    {
        if(IsDragging)
        {
            if(pressedObject=="cat")
            {
                Engine.DragTo(new Spot(p.X-grabOffset.X,p.Y-grabOffset.Y));
            }
            else Engine.MoveObject(pressedObject,new Spot(p.X-grabOffset.X,p.Y-grabOffset.Y));
        }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if(!pressed)return;
        pointer=World(e.GetPosition(this));MoveDragged(pointer);
        bool dragged=IsDragging;pressed=false;IsDragging=false;Engine.Holding=false;
        if(IsMouseCaptured)ReleaseMouseCapture();Cursor=Cursors.Arrow;
        if(dragged){if(pressedObject=="cat")Engine.Drop(AtNest);}
        else if((World(e.GetPosition(this))-pressedPoint).Length<12)
        {
            if(pressedObject=="cat")Engine.Interact();
            else if(pressedObject!="nest")Engine.Refill(pressedObject);
        }
        SaveNow?.Invoke();e.Handled=true;
    }
    private void ReturnWand(){wandHeld=false;Engine.SetToy(false,new Spot());Cursor=Cursors.Arrow;if(IsMouseCaptured)ReleaseMouseCapture();SaveNow?.Invoke();InvalidateVisual();}
    internal bool OpensSettingsAt(Point p)=>!wandHeld&&!pressed&&!CatRect.Contains(p)&&!WandRect.Contains(p)&&!LitterRect.Contains(p)&&!ObjectRect(Engine.FoodSpot).Contains(p)&&!ObjectRect(Engine.WaterSpot).Contains(p)&&NestRect.Contains(p);
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {if(OpensSettingsAt(World(e.GetPosition(this))))OpenSettings?.Invoke();e.Handled=true;}
    protected override void OnRender(DrawingContext context)
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        base.OnRender(context);
        using var art=artwork.RenderOpen();using var ui=overlay.RenderOpen();
        var dc=art;
        softShadow.BlurRadius=14*Scale;softShadow.ShadowDepth=3*Scale;
        RenderArtwork(dc);RenderOverlay(ui);
        RenderMilliseconds+=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;RenderCount++;
    }
    internal void ExportAnimationAudit(string directory,string? filter,bool resume=false)
    {
        Directory.CreateDirectory(directory);
        var selected=filter?.Split(',').Select(x=>x.StartsWith("video-")?x:"video-"+x).ToHashSet();
        var records=new List<object>();
        foreach(string id in framePaths.Keys.Where(x=>x.StartsWith("video-")&&(selected is null||selected.Contains(x))).OrderBy(x=>x))
        {
            IReadOnlyList<BitmapSource>? images=null;string folder=Path.Combine(directory,id);Directory.CreateDirectory(folder);
            for(int index=0;index<framePaths[id].Count;index++)
            {
                var frame=playback.InspectFrame(id,index)??throw new InvalidDataException("Missing clip "+id);
                string relative=$"{id}/{index:0000}.png";
                if(resume&&File.Exists(Path.Combine(directory,relative)))
                {records.Add(new {Clip=id,Index=index,File=relative,Definition=frame.Definition});continue;}
                images??=GetFrames(id)!;
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                {
                    dc.PushTransform(new ScaleTransform(2,2));dc.PushTransform(new TranslateTransform(192,256));
                    DrawVideoFrame(dc,images[index],frame.Definition);dc.Pop();dc.Pop();
                }
                var bitmap=new RenderTargetBitmap(768,640,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using(var stream=File.Create(Path.Combine(directory,relative)))encoder.Save(stream);
                records.Add(new {Clip=id,Index=index,File=relative,Definition=frame.Definition});
                // Bound the unmanaged WPF surfaces used by offline audits.
                if(index%24==23){GC.Collect();GC.WaitForPendingFinalizers();}
            }
        }
        if(records.Count==0)throw new InvalidDataException("No selected animation frames");
        File.WriteAllText(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(new {PixelsPerUnit=2,RootX=192,RootY=256,Frames=records}));
    }
    internal void ExportNestOcclusionAudit(string directory)
    {
        Directory.CreateDirectory(directory);Engine.Layout(900,450);Engine.MoveObject("nest",new Spot(450,Engine.GroundY));
        var records=new List<object>();int oldWorst=0,newWorst=0;
        RenderTargetBitmap Render(Action<DrawingContext> draw)
        {var visual=new DrawingVisual();using(var dc=visual.RenderOpen())draw(dc);var bitmap=new RenderTargetBitmap(900,450,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);return bitmap;}
        byte[] Pixels(BitmapSource bitmap){var bytes=new byte[900*450*4];bitmap.CopyPixels(bytes,900*4,0);return bytes;}
        void Save(BitmapSource bitmap,string name){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(directory,name));encoder.Save(stream);}
        foreach(int side in new[]{-1,1})for(int i=0;i<=40;i++)
        {
            string id=side<0?"video-right":"video-14";var images=GetFrames(id)!;int index=(i*3)%images.Count;
            double x=Engine.Nest.X-InteractionGeometry.NestRestOffsetX+side*(180-i*4.5);
            double footY=Engine.GroundY-InteractionGeometry.NestSupport(x,Engine.Nest.X);
            var sprite=playback.InspectFrame(id,index)!.Value;
            void Cat(DrawingContext dc){dc.PushTransform(new TranslateTransform(x,footY));DrawVideoFrame(dc,images[index],sprite.Definition);dc.Pop();}
            void Foreground(DrawingContext dc,bool legacy)
            {
                if(!InteractionGeometry.InsideNestSeat(x,Engine.Nest.X))return;
                if(!legacy){DrawNest(dc,true);return;}
                // Negative control: the previous high-arm mask must fail.
                var p=Engine.Nest;dc.PushTransform(new ScaleTransform(InteractionGeometry.NestScale,InteractionGeometry.NestScale,p.X,p.Y));
                var shape=System.Windows.Media.Geometry.Parse("M -98,-82 C -94,-101 -74,-113 -54,-109 C -58,-94 -66,-87 -67,-72 C -63,-57 -37,-46 0,-46 C 36,-45 59,-50 67,-67 C 64,-85 60,-95 65,-109 C 84,-101 96,-88 98,-70 L 98,0 L -98,0 Z").Clone();
                shape.Transform=new TranslateTransform(p.X,p.Y);dc.PushClip(shape);dc.DrawImage(nestSprite,new Rect(p.X-98,p.Y-146,196,146));dc.Pop();dc.Pop();
            }
            var cat=Pixels(Render(Cat));var oldLayer=Pixels(Render(dc=>Foreground(dc,true)));var newLayer=Pixels(Render(dc=>Foreground(dc,false)));
            int oldCovered=0,newCovered=0;
            // Allow the front lip to cover the bottom 12 logical units of paws;
            // it must never remove the upper leg, torso or tail root.
            for(int y=0;y<Math.Min(450,footY-12);y++)for(int px=0;px<900;px++)
            {int alpha=(y*900+px)*4+3;if(cat[alpha]>128){if(oldLayer[alpha]>128)oldCovered++;if(newLayer[alpha]>128)newCovered++;}}
            oldWorst=Math.Max(oldWorst,oldCovered);newWorst=Math.Max(newWorst,newCovered);
            records.Add(new {Side=side,Step=i,Clip=id,Frame=index,X=x,FootY=footY,OldBodyCovered=oldCovered,NewBodyCovered=newCovered});
            if(i is 26 or 30 or 34 or 40)
                foreach(bool legacy in new[]{true,false})Save(Render(dc=>{DrawNest(dc,false);Cat(dc);Foreground(dc,legacy);}),$"{(side<0?"left":"right")}-{i}-{(legacy?"before":"after")}.png");
        }
        File.WriteAllText(Path.Combine(directory,"occlusion.json"),JsonSerializer.Serialize(new {OldWorst=oldWorst,NewWorst=newWorst,Samples=records}));
        if(oldWorst<100||newWorst!=0)throw new InvalidDataException("Nest foreground audit failed; inspect occlusion.json");
    }
    public void SavePreview(string path)
    {
        var scene=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);scene.Render(this);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brush(DarkPreview?"#181B22":"#E6E8DF"),null,new Rect(0,0,ActualWidth,ActualHeight));dc.DrawImage(scene,new Rect(0,0,ActualWidth,ActualHeight));}
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
