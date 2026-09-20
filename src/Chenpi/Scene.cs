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
    private readonly Dictionary<string,List<BitmapSource>> frames=new();
    private readonly Dictionary<string,List<string>> framePaths=new();
    private readonly Queue<string> decodedClips=new();
    private readonly Dictionary<BitmapSource,Rect> frameBounds=new();
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
    private List<BitmapSource>? GetFrames(string id)
    {
        if(frames.TryGetValue(id,out var cached))return cached;
        if(!framePaths.TryGetValue(id,out var paths))return null;
        var images=new List<BitmapSource>();
        foreach(var full in paths)
        {
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.UriSource=new Uri(full);bitmap.EndInit();bitmap.Freeze();images.Add(bitmap);
        }
        frames[id]=images;decodedClips.Enqueue(id);
        while(decodedClips.Count>3)frames.Remove(decodedClips.Dequeue());
        return images;
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
                    if(!File.Exists(full))throw new FileNotFoundException(full);images.Add(full);
                }
                if(images.Count>0)framePaths[property.Name]=images;
                }
                catch(Exception ex){System.Diagnostics.Trace.WriteLine("Animation fallback: "+property.Name+": "+ex.Message);}
            }
            variants.Load(doc.RootElement,id=>framePaths.ContainsKey(id));
            playback.Load(doc.RootElement,id=>framePaths.TryGetValue(id,out var set)?set.Count:0);
            if(doc.RootElement.TryGetProperty("wallContactOffsets",out var wall))
            {Engine.WallRightOffset=wall.GetProperty("right").GetDouble();Engine.WallLeftOffset=wall.GetProperty("left").GetDouble();}
            if(doc.RootElement.TryGetProperty("relaxedHalfWidth",out var footprint))Engine.RelaxedHalfWidth=footprint.GetDouble();
            bool prepared=doc.RootElement.TryGetProperty("videoMattePrepared",out var readyMatte)&&readyMatte.GetBoolean();
            // Decode before showing the window. Switching a clip does zero disk IO/decode.
            bool AlreadyExported(KeyValuePair<string,List<string>> p)=>resumeFrames is not null&&Enumerable.Range(0,p.Value.Count).All(i=>File.Exists(Path.Combine(resumeFrames,p.Key,$"{i:0000}.png")));
            Parallel.ForEach(framePaths.Where(p=>p.Key.StartsWith("video-",StringComparison.Ordinal)&&(selected is null||selected.Contains(p.Key))&&!AlreadyExported(p)),
                new ParallelOptions{MaxDegreeOfParallelism=2},entry=>{
                    var decoded=new List<BitmapSource>();
                    foreach(string file in entry.Value)
                    {
                        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
                        bitmap.UriSource=new Uri(file);bitmap.EndInit();bitmap.Freeze();
                        var cleaned=PrepareBitmap(bitmap,true,out var bounds,1,!prepared);decoded.Add(cleaned);
                        lock(frameBounds)frameBounds[cleaned]=bounds;
                    }
                    lock(frames)frames[entry.Key]=decoded;
                });
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
        dc.PushTransform(new ScaleTransform(Scale,Scale));
        if(PreviewSupplies)
        {
            for(int n=0;n<=5;n++)
            {
                double x=80+n*150;
                DrawBowl(dc,new Spot(x,140),n*20,false,0);
                DrawBowl(dc,new Spot(x,250),n*20,true,0);
                DrawLitterLevel(dc,new Spot(x,360),n*20);
            }
            for(int n=0;n<3;n++)DrawLitterLevel(dc,new Spot(170+n*310,480),20,new[]{n/2d,0,0,0,0});
            dc.Pop();return;
        }
        if(PreviewRightWalk)
        {
            DrawCat(dc,100+(Engine.Now*45)%Math.Max(1,WorldWidth-200),WorldHeight*.66,"walk",Engine.Now,false);
            dc.Pop();return;
        }
        Engine.Support.Update(Engine);
        // Cross the side in front of its arm. Only the seated body belongs
        // behind the rim; otherwise the arm cuts through a climbing cat.
        nestOcclusion=Engine.Support.Kind=="nest"&&Engine.Action!="drag"&&InteractionGeometry.InsideNestSeat(Engine.VisualPosition.X,Engine.Nest.X);
        DrawNest(dc,false);
        bool eating=Engine.Action=="eat",drinking=Engine.Action=="drink",usingLitter=Engine.Support.Kind=="litter"&&Engine.Action!="drag"&&Engine.Support.Height>=InteractionGeometry.LitterSurfaceLift-.01;
        if(eating)DrawCareBowl(dc,Engine.FoodSpot,shownFood,false,false);
        if(drinking)DrawCareBowl(dc,Engine.WaterSpot,shownWater,true,false);
        DrawLitter(dc);
        DrawCat(dc,Engine.VisualPosition.X,Engine.VisualPosition.Y-lift,Engine.AligningForCare?"care-ready":Engine.Action,Engine.ActionTime,Engine.FacingLeft);
        if(nestOcclusion)DrawNest(dc,true);
        if(usingLitter)
        {
            var p=Engine.LitterSpot;
            // Only the tray's front wall occludes paws; the back rim stays behind.
            dc.PushClip(System.Windows.Media.Geometry.Parse(FormattableString.Invariant($"M {p.X-InteractionGeometry.LitterHalfWidth},{p.Y-60} Q {p.X},{p.Y-8} {p.X+InteractionGeometry.LitterHalfWidth},{p.Y-60} L {p.X+InteractionGeometry.LitterHalfWidth},{p.Y} L {p.X-InteractionGeometry.LitterHalfWidth},{p.Y} Z")));
            DrawLitter(dc);dc.Pop();
        }
        // Bowls stay in front of the cat on this single ground plane. Only
        // the bowl being used is split at its curved opening, never at a flat cut.
        if(eating)DrawCareBowl(dc,Engine.FoodSpot,shownFood,false,true);
        else DrawBowl(dc,Engine.FoodSpot,shownFood,false,Engine.Now);
        if(drinking)DrawCareBowl(dc,Engine.WaterSpot,shownWater,true,true);
        else DrawBowl(dc,Engine.WaterSpot,shownWater,true,Engine.Now);
        DrawWand(dc);
        dc.Pop();dc=ui;dc.PushTransform(new ScaleTransform(Scale,Scale));
        if(IsDragging&&pressedObject=="cat"&&AtNest)
        {dc.DrawRoundedRectangle(null,new Pen(Brush("#A3C7A2"),3),NestRect,30,30);}
        var age=DateTimeOffset.UtcNow-Engine.State.AdoptedAt;
        string span=age.TotalDays>=1?$"相伴 {Math.Max(0,(int)age.TotalDays)} 天":$"相伴 {Math.Max(0,(int)age.TotalHours):00}:{Math.Max(0,age.Minutes):00}";
        LabelPill(dc,span,Engine.Nest.X,Engine.Nest.Y+12,102);
        dc.Pop();
        RenderMilliseconds+=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;RenderCount++;
    }
    private void DrawWand(DrawingContext dc)
    {
        var tip=wandHeld?Engine.ToyTip:Engine.WandHome;
        // Detection circles are logic-only. No rings, radius labels, or range overlays are rendered.
        // The feather tip is the collision center and follows the pointer without easing.
        Line(dc,tip.X+8,tip.Y+6,tip.X+34,tip.Y+35,"#A07852",5);
        Line(dc,tip.X+8,tip.Y+6,tip.X-6,tip.Y-12,"#DBD6C9",1.5);
        dc.PushTransform(new RotateTransform(-30+Math.Sin(Engine.Now*5)*8,tip.X,tip.Y));
        dc.DrawEllipse(Brush("#DCB5BC"),null,new Point(tip.X-7,tip.Y-13),7,18);
        dc.DrawEllipse(Brush("#9ABEB9"),null,new Point(tip.X+3,tip.Y-17),7,20);
        Line(dc,tip.X-3,tip.Y+3,tip.X+3,tip.Y-30,"#F4EDE1",1);
        dc.Pop();
    }
    private void DrawNest(DrawingContext dc,bool foreground)
    {
        var p=Engine.Nest;
        dc.PushTransform(new ScaleTransform(InteractionGeometry.NestScale,InteractionGeometry.NestScale,p.X,p.Y));
        DrawNestUnscaled(dc,foreground);dc.Pop();
    }
    private void DrawNestUnscaled(DrawingContext dc,bool foreground)
    {
        var p=Engine.Nest;
        if(nestSprite is not null)
        {
            if(foreground)
            {
                // High side bolsters belong behind a cat crossing into the seat.
                // Only the low front lip may cover the planted paws. Promoting
                // the whole side arm here severs the trailing leg and tail.
                var rim=System.Windows.Media.Geometry.Parse("M -98,-34 Q -82,-39 -67,-53 C -48,-46 -25,-46 0,-46 C 25,-46 48,-46 67,-53 Q 82,-39 98,-34 L 98,0 L -98,0 Z").Clone();
                rim.Transform=new TranslateTransform(p.X,p.Y);dc.PushClip(rim);
            }
            dc.DrawImage(nestSprite,new Rect(p.X-98,p.Y-146,196,146));
            if(foreground)dc.Pop();return;
        }
        if(!foreground)
        {
            Shadow(dc,p.X,p.Y+17,85,13);
            dc.DrawRoundedRectangle(Brush("#AE8062"),new Pen(Brush("#865D46"),2),new Rect(p.X-79,p.Y-82,158,105),38,38);
            dc.DrawEllipse(Brush("#E2C9AC"),null,new Point(p.X,p.Y-28),70,40);
            dc.DrawEllipse(Brush("#F3E5CF"),new Pen(Brush("#CBAF8C"),1),new Point(p.X,p.Y-25),62,29);
            for(int i=0;i<5;i++)dc.DrawLine(new Pen(Brush("#BC9274"),2),new Point(p.X-62+i*29,p.Y+4),new Point(p.X-62+i*29,p.Y+19));
        }
        else dc.DrawRoundedRectangle(Brush("#C49776"),new Pen(Brush("#A67C5D"),1.5),new Rect(p.X-79,p.Y-6,158,29),12,12);
    }
    private static void DrawBowl(DrawingContext dc,Spot p,double fill,bool water,double time)
    {
        dc.PushTransform(new TranslateTransform(0,InteractionGeometry.BowlBaseOffset(water)));
        // Align the visible base (excluding source-image transparent padding).
        p=new Spot(p.X,p.Y-(water?19:22));
        dc.PushTransform(new ScaleTransform(BowlScale,BowlScale,p.X,p.Y));
        DrawBowlFullSize(dc,p,fill,water,time);dc.Pop();dc.Pop();
    }
    private void DrawCareBowl(DrawingContext dc,Spot p,double fill,bool water,bool front)
    {
        var foreground=BowlForeground(p,water);
        System.Windows.Media.Geometry clip=front?foreground:new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new Rect(p.X-40,InteractionGeometry.BowlY(p.Y,p.Y-80,water),80,82)),foreground);
        dc.PushClip(clip);DrawBowl(dc,p,fill,water,Engine.Now);dc.Pop();
    }
    private static System.Windows.Media.Geometry BowlForeground(Spot p,bool water)
    {
        double side=water?25:27,edge=water?43:44,center=water?31:27;
        var shape=System.Windows.Media.Geometry.Parse(FormattableString.Invariant($"M {p.X-40},{p.Y-80} L {p.X-side},{p.Y-edge} Q {p.X},{p.Y-(2*center-edge)} {p.X+side},{p.Y-edge} L {p.X+40},{p.Y-80} L {p.X+40},{p.Y+2} L {p.X-40},{p.Y+2} Z")).Clone();
        shape.Transform=new TranslateTransform(0,InteractionGeometry.BowlBaseOffset(water));return shape;
    }
    private bool FrontBowlContains(Point at,bool water)
    {
        var bitmap=water?waterCup:foodBowl;if(bitmap is null)return false;
        var p=water?Engine.WaterSpot:Engine.FoodSpot;
        var rect=new Rect(p.X-36,InteractionGeometry.BowlY(p.Y,p.Y-(water?61:65.8),water),72,72);
        if(!rect.Contains(at))return false;
        bool inUse=Engine.Action==(water?"drink":"eat");
        if(inUse&&CatRect.Contains(at)&&!BowlForeground(p,water).FillContains(at))return false;
        int x=Math.Clamp((int)((at.X-rect.X)/rect.Width*bitmap.PixelWidth),0,bitmap.PixelWidth-1);
        int y=Math.Clamp((int)((at.Y-rect.Y)/rect.Height*bitmap.PixelHeight),0,bitmap.PixelHeight-1);
        var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);
        return pixel[3]>32;
    }
    private static void DrawBowlFullSize(DrawingContext dc,Spot p,double fill,bool water,double time)
    {
        int layers=PetEngine.SupplyLayers(fill);
        if(water&&waterCup is not null)
        {
            dc.DrawImage(waterCup,new Rect(p.X-60,p.Y-70,120,120));
            if(layers>0)
            {
                dc.PushClip(new EllipseGeometry(new Point(p.X,p.Y-29),37,16));
                double y=p.Y-18-layers*2,rx=20+layers*3,ry=4+layers*1.7;
                dc.DrawEllipse(Brush("#889CCFD3"),new Pen(Brush("#AAB4D8D6"),.6),new Point(p.X,y),rx,ry);
                dc.DrawLine(new Pen(Brush("#CDEDF5EA"),.9),new Point(p.X-rx*.5,y-ry*.2),new Point(p.X-rx*.12,y-ry*.35));
                dc.DrawLine(new Pen(Brush("#88EDF5EA"),.6),new Point(p.X+rx*.25,y+ry*.2),new Point(p.X+rx*.5,y+ry*.13));
                dc.Pop();
            }
            return;
        }
        if(!water&&foodBowl is not null&&kibble is not null)
        {
            dc.DrawImage(foodBowl,new Rect(p.X-60,p.Y-73,120,120));
            dc.PushClip(new EllipseGeometry(new Point(p.X,p.Y-26),36,25));
            // Small stable scattered pellets, with narrower upper tiers.
            // Far pellets draw first; each refill adds a tier without rearranging old ones.
            for(int layer=0;layer<layers;layer++)
            {
                double radius=25-layer*2.5;
                for(int row=0;row<3;row++)
                {
                    int count=row==1?5:4;
                    for(int i=0;i<count;i++)
                    {
                        double x=p.X+(i-(count-1)/2d)*radius*2/count+Math.Sin(i*7+row*3+layer)*1.4;
                        double y=p.Y-24+row*4.5-layer*3.8+Math.Sin(i*3+layer)*.8;
                        double size=9+(i*7+row*3+layer)%3;
                        dc.PushTransform(new RotateTransform((i*47+row*29+layer*19)%120-60,x,y));
                        dc.DrawImage(kibble,new Rect(x-size/2,y-size*.36,size,size*.72));dc.Pop();
                    }
                }
            }
            dc.Pop();return;
        }
        Shadow(dc,p.X,p.Y+25,48,8);
        var body=water?"#9EBABB":"#CDB18D";var rim=water?"#D0E1DD":"#ECDBC1";
        Geometry(dc,$"M {p.X-41},{p.Y-8} L {p.X-32},{p.Y+25} Q {p.X},{p.Y+38} {p.X+32},{p.Y+25} L {p.X+41},{p.Y-8} Z",body);
        dc.DrawEllipse(Brush(rim),new Pen(Brush(water?"#688C90":"#A58A65"),1.5),new Point(p.X,p.Y-6),42,17);
        dc.DrawEllipse(Brush(water?"#5E8F9B":"#887055"),null,new Point(p.X,p.Y-5),34,11);
        if(fill>0)
        {
            if(water)
            {
                double level=layers/5d;
                dc.DrawEllipse(Brush("#9DD7DF"),null,new Point(p.X,p.Y-1-level*5),18+level*14,2+level*8);
                dc.DrawLine(new Pen(Brush("#DDF6F1"),1.5),new Point(p.X-12+Math.Sin(time*2)*2,p.Y-2-level*5),new Point(p.X+1+Math.Sin(time*2)*2,p.Y-2-level*5));
            }
            else for(int i=0;i<(int)Math.Ceiling(fill/6);i++)dc.DrawEllipse(Brush(i%2==0?"#BD854D":"#D5A265"),null,new Point(p.X-25+(i*17%53),p.Y-10+(i*7%13)),4,3);
        }
    }
    private void DrawLitter(DrawingContext dc)
    {
        DrawLitterLevel(dc,Engine.LitterSpot,Engine.State.Litter,Engine.State.LitterCover);
    }
    private static void DrawLitterLevel(DrawingContext dc,Spot p,double quantity,double[]? covers=null)
    {
        dc.PushTransform(new ScaleTransform(InteractionGeometry.LitterScaleX,InteractionGeometry.LitterScale,p.X,p.Y));
        DrawLitterUnscaled(dc,p,quantity,covers);dc.Pop();
    }
    private static void DrawLitterUnscaled(DrawingContext dc,Spot p,double quantity,double[]? covers)
    {
        p=new Spot(p.X,p.Y-28);
        if(litterSprite is not null)
        {
            dc.DrawImage(litterSprite,new Rect(p.X-56,p.Y-34,112,62));
            for(int i=0;i<PetEngine.SupplyLayers(quantity);i++)
            {
                var clump=InteractionGeometry.LitterClump(i);
                double x=p.X+clump.X/InteractionGeometry.LitterScaleX,y=p.Y+28+clump.Y/InteractionGeometry.LitterScale;
                DrawClump(dc,x,y,covers is not null&&i<covers.Length?covers[i]:0);
            }
            return;
        }
        Shadow(dc,p.X,p.Y+26,59,9);
        dc.DrawRoundedRectangle(Brush("#A4AF9C"),new Pen(Brush("#79836F"),1.5),new Rect(p.X-56,p.Y-27,112,56),15,15);
        dc.DrawRoundedRectangle(Brush("#E3D8BE"),new Pen(Brush("#C2B79C"),1),new Rect(p.X-47,p.Y-21,94,32),11,11);
        for(int i=0;i<25;i++)dc.DrawEllipse(Brush("#C5B79B"),null,new Point(p.X-39+(i*17%78),p.Y-15+(i*11%21)),1.5,1);
        for(int i=0;i<PetEngine.SupplyLayers(quantity);i++)
        {
            var clump=InteractionGeometry.LitterClump(i);
            DrawClump(dc,p.X+clump.X/InteractionGeometry.LitterScaleX,p.Y+28+clump.Y/InteractionGeometry.LitterScale,covers is not null&&i<covers.Length?covers[i]:0);
        }
    }
    private static void DrawClump(DrawingContext dc,double x,double y,double cover)
    {
        dc.DrawEllipse(Brush("#65503C"),null,new Point(x,y),6,3.5);
        dc.DrawEllipse(Brush("#8C7156"),null,new Point(x-1,y-.8),4,2);
        if(cover<=0)return;
        // A growing opaque sand mound leaves the unburied part visible.
        dc.PushClip(new RectangleGeometry(new Rect(x-8,y-6,16*Math.Clamp(cover,0,1),12)));
        dc.DrawEllipse(Brush("#C5BBA4"),new Pen(Brush("#A89B80"),.4),new Point(x,y-.4),7.5,4.5);
        for(int j=0;j<12;j++)dc.DrawEllipse(Brush(j%2==0?"#DDD3BD":"#AFA287"),null,new Point(x-5+(j*7%11),y-2+(j*3%5)),.65,.45);
        dc.Pop();
    }
    private void DrawVideoFrame(DrawingContext dc,BitmapSource image,SpriteClip definition)
    {
        var b=frameBounds.TryGetValue(image,out var bounds)?bounds:new Rect(0,0,1,1);
        dc.DrawImage(image,new Rect((b.X-definition.AnchorX)*definition.Width,(b.Y-definition.AnchorY)*definition.Height,b.Width*definition.Width,b.Height*definition.Height));
    }
    internal void ExportAnimationAudit(string directory,string? filter,bool resume=false)
    {
        Directory.CreateDirectory(directory);
        var selected=filter?.Split(',').Select(x=>x.StartsWith("video-")?x:"video-"+x).ToHashSet();
        var records=new List<object>();
        foreach(string id in framePaths.Keys.Where(x=>x.StartsWith("video-")&&(selected is null||selected.Contains(x))).OrderBy(x=>x))
        {
            List<BitmapSource>? images=null;string folder=Path.Combine(directory,id);Directory.CreateDirectory(folder);
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
    private void DrawCat(DrawingContext dc,double x,double y,string action,double time,bool left)
    {
        if(action==Engine.Action&&spriteRevision!=Engine.ActionRevision)
        {spriteRevision=Engine.ActionRevision;selectedVariant=variants.Choose(action);}
        var variant=action==Engine.Action?selectedVariant:null;
        var sample=playback.Sample(action,Engine.Now,left);
        if(variant is null&&sample is SpriteFrame sprite&&GetFrames(sprite.Clip) is {} generated)
        {
            poseLift=Engine.Grounded?Engine.Support.Height+InteractionGeometry.SurfaceLift(sprite.Clip,sprite.Index/(double)Math.Max(1,sprite.Definition.Count-1),false,action=="drag"?"drag":""):0;
            if(Engine.Grounded&&SpritePlayback.AuthoredCarryLift(sprite) is double carryLift)poseLift=Engine.Support.Height+carryLift;
            if(Engine.Grounded&&action=="land"&&playback.CarriesExpression)poseLift+=InteractionGeometry.PickupLift*(1-Math.Clamp(Engine.ActionTime/.7,0,1));
            y-=poseLift;
            DisplayedClip=sprite.Clip;DisplayedFrame=sprite.Index;
            var definition=sprite.Definition;
            DisplayedDefinition=definition;
            if(frameBounds.TryGetValue(generated[sprite.Index],out var bounds))
                currentCatBounds=new Rect((bounds.X-definition.AnchorX)*definition.Width,(bounds.Y-definition.AnchorY)*definition.Height,bounds.Width*definition.Width,bounds.Height*definition.Height);
            if(lastSpriteClip!=sprite.Clip)
            {
                lastSpriteClip=sprite.Clip;
                if(ClipTransitions.Count>=100)ClipTransitions.RemoveAt(0);
                ClipTransitions.Add(new {Time=Engine.Now,Clip=sprite.Clip,X=x,Y=y,definition.Width,definition.Height,definition.AnchorX,definition.AnchorY});
            }
            bool mirror=left&&definition.MirrorWithFacing;
            dc.PushTransform(new TranslateTransform(x,y));if(mirror)dc.PushTransform(new ScaleTransform(-1,1));
            // One supplied frame per instant. Layering the opaque previous pose
            // underneath a fading new pose created double outlines and bright flashes.
            DrawVideoFrame(dc,generated[sprite.Index],definition);
            if(mirror)dc.Pop();dc.Pop();if(action==Engine.Action)FramePresented?.Invoke(sprite);return;
        }
        poseLift=0;lastSpriteClip="";currentCatBounds=null;
        DisplayedClip=variant?.Id??action;DisplayedFrame=0;
        if(action==Engine.Action)FramePresented?.Invoke(null);
        if(GetFrames(variant?.Id??action) is {} set&&set.Count>0)
        {
            dc.PushTransform(new TranslateTransform(x,y));if(left)dc.PushTransform(new ScaleTransform(-1,1));
            double index=Math.Floor(time*(variant?.Fps??fps));
            int frame=variant?.Loop==false?(int)Math.Min(index,set.Count-1):(int)(index%set.Count);
            dc.DrawImage(set[frame],new Rect(-80,-142,160,160));if(left)dc.Pop();dc.Pop();return;
        }
        bool sleep=action=="sleep",walking=action is "walk" or "toy-run" or "request-walk" or "guide-walk",drag=action=="drag",bow=action is "eat" or "drink",toilet=action is "toilet" or "bury";
        double gait=action=="toy-run"?17:11;
        double bob=walking?Math.Sin(time*gait)*(action=="toy-run"?3:2):Math.Sin(Engine.Now*2)*.8;
        Shadow(dc,x,y+4,46,7);
        dc.PushTransform(new TranslateTransform(x,y+bob));
        bool leaning=action is "rub" or "roll";
        if(leaning)dc.PushTransform(new RotateTransform(action=="roll"?Math.Sin(time*3)*65:Math.Sin(time*5)*10,0,-35));
        if(left)dc.PushTransform(new ScaleTransform(-1,1));
        var fur=Brush("#8292A7");var outline=new Pen(Brush("#566377"),1.5);var light=Brush("#A5B2C2");
        if(sleep)
        {
            dc.DrawEllipse(fur,outline,new Point(0,-27),53,30);
            Geometry(dc,"M 28,-17 Q 64,-17 48,-46 Q 42,-53 34,-46 Q 55,-35 25,-30","#96A4B7");
            dc.DrawEllipse(light,null,new Point(-14,-26),23,17);
            Geometry(dc,"M -43,-42 L -45,-66 L -24,-49 Z","#8292A7");
            dc.DrawEllipse(fur,outline,new Point(-30,-40),29,23);
            Line(dc,-48,-43,-38,-40,"#344256",2);Line(dc,-29,-40,-19,-43,"#344256",2);
            for(int i=0;i<3;i++)dc.DrawEllipse(Brush("#AED8E4EF"),null,new Point(30+i*8,-73-i*10-Math.Sin(Engine.Now*1.5)*3),2+i,2+i);
        }
        else
        {
            double tail=Math.Sin(Engine.Now*3)*6;
            var tailPath=new StreamGeometry();using(var ctx=tailPath.Open()){ctx.BeginFigure(new Point(33,-30),false,false);ctx.QuadraticBezierTo(new Point(70+tail,-22),new Point(65,-62),true,false);ctx.QuadraticBezierTo(new Point(62,-75),new Point(54,-68),true,false);}tailPath.Freeze();
            dc.DrawGeometry(null,new Pen(fur,15){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},tailPath);
            dc.DrawEllipse(fur,outline,new Point(0,toilet?-29:action=="sit"?-32:-38),walking?40:34,toilet||action=="sit"?34:42);
            dc.DrawEllipse(light,null,new Point(-3,-29),21,26);
            for(int i=0;i<2;i++)
            {
                bool requesting=action is "request-food" or "request-water" or "request-litter";
                bool indicating=action is "guide-food" or "guide-water" or "guide-litter";
                bool reaching=((action=="paw"||requesting)&&i==0)||((action=="toy-bat"||indicating)&&i==1);
                double leg=walking?Math.Sin(time*gait+i*Math.PI)*7:reaching?-12-Math.Abs(Math.Sin(time*9))*18:0;
                double reach=action=="toy-bat"&&i==1?Math.Abs(Math.Sin(time*9))*16:0;
                dc.DrawRoundedRectangle(fur,outline,new Rect(-29+i*35+reach,-18+leg,24,drag?20+Math.Clamp(lift/8,0,1)*16:20-leg),10,10);
                Line(dc,-22+i*35,0+leg,-22+i*35,4+leg,"#67778C",1);
            }
            double headY=bow?-55:-88,headX=bow?-24:-5;
            if(action=="wake")headY+=Math.Sin(time*2)*10;
            if(action=="guide-look")headX+=Math.Sin(time*3)*10;
            dc.PushTransform(new TranslateTransform(headX,headY));
            Geometry(dc,"M -38,-8 L -37,-44 Q -22,-41 -13,-21 Z","#8292A7","#566377");
            Geometry(dc,"M 13,-20 Q 24,-42 37,-44 L 38,-4 Z","#8292A7","#566377");
            Geometry(dc,"M -32,-17 L -32,-34 L -20,-23 Z","#C1A4A7");
            Geometry(dc,"M 21,-22 L 32,-34 L 32,-15 Z","#C1A4A7");
            dc.DrawEllipse(fur,outline,new Point(0,0),45,34);
            dc.DrawEllipse(light,null,new Point(-10,10),27,18);
            bool eyesClosed=action is "pet" or "rub" or "roll" or "care-thanks"||bow||(time%6>5.75);
            for(int i=0;i<2;i++)
            {
                double eyeX=-21+i*40;
                if(eyesClosed)Geometry(dc,$"M {eyeX-7},-3 Q {eyeX},-10 {eyeX+7},-3",null,"#344256",2.5);
                else
                {
                    dc.DrawEllipse(Brush("#E5C080"),new Pen(Brush("#4E5968"),1),new Point(eyeX,-3),9,11);
                    dc.DrawEllipse(Brush("#263542"),null,new Point(eyeX-1,-3),3.5,8);
                    dc.DrawEllipse(Brush("#FFF8E9"),null,new Point(eyeX+2,-7),2,2);
                }
            }
            Geometry(dc,"M -5,10 L 5,10 L 0,15 Z","#AF868A");
            Geometry(dc,"M 0,15 Q -5,23 -10,17 M 0,15 Q 5,23 10,17",null,"#586172",1.5);
            for(int i=0;i<2;i++){Line(dc,-26,13+i*5,-48,8+i*13,"#C8D0DA",1);Line(dc,26,13+i*5,48,8+i*13,"#C8D0DA",1);}
            dc.Pop();
            if(action is "pet" or "rub" or "roll" or "care-thanks")Label(dc,"♥",38,-132+Math.Sin(time*3)*5,25,"#DC9A91",true);
            if(action is "request-food" or "request-water" or "request-litter" or "guide-food" or "guide-water" or "guide-litter")
            {
                // Wordless signals: point, lick lips, or paw at the ground.
                double reach=Math.Sin(time*5)*9;
                if(action.EndsWith("water"))dc.DrawEllipse(Brush("#D6A5AA"),null,new Point(-5,-64),4,5+Math.Abs(reach)*.25);
                else if(action.EndsWith("food"))dc.DrawEllipse(fur,outline,new Point(40,-42+reach),11,7);
                else {dc.DrawEllipse(fur,outline,new Point(-38-reach,-6),13,6);for(int i=0;i<3;i++)dc.DrawEllipse(Brush("#DCCBB0"),null,new Point(-50-i*6,-5-Math.Abs(Math.Sin(time*5+i))*12),2,2);}
            }
            if(action=="bury")for(int i=0;i<3;i++)dc.DrawEllipse(Brush("#DCCBB0"),null,new Point(42+i*7,-12-Math.Abs(Math.Sin(time*9+i))*22),3,2);
        }
        if(left)dc.Pop();if(leaning)dc.Pop();dc.Pop();
    }
    private static SolidColorBrush Brush(string hex){if(brushes.TryGetValue(hex,out var found))return found;var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();brushes[hex]=b;return b;}
    private static void Shadow(DrawingContext dc,double x,double y,double rx,double ry)=>dc.DrawEllipse(Brush("#20000000"),null,new Point(x,y),rx,ry);
    private static void Line(DrawingContext dc,double x1,double y1,double x2,double y2,string color,double width)=>dc.DrawLine(new Pen(Brush(color),width),new Point(x1,y1),new Point(x2,y2));
    private static void Geometry(DrawingContext dc,string data,string? fill,string? stroke=null,double width=1.5)
    {
        if(!shapes.TryGetValue(data,out var shape)){shape=System.Windows.Media.Geometry.Parse(data);shape.Freeze();if(shapes.Count>=512)shapes.Clear();shapes[data]=shape;}
        dc.DrawGeometry(fill is null?null:Brush(fill),stroke is null?null:new Pen(Brush(stroke),width),shape);
    }
    private static void Label(DrawingContext dc,string value,double x,double y,double size,string color,bool centered=false)
    {
        var key=(value,size,color);
        if(!labels.TryGetValue(key,out var text)){text=new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,Brush(color),1);if(labels.Count>=128)labels.Clear();labels[key]=text;}
        dc.DrawText(text,new Point(centered?x-text.Width/2:x,y));
    }
    private static void LabelPill(DrawingContext dc,string value,double x,double y,double width)
    {dc.DrawRoundedRectangle(Brush("#EAF9F4EB"),null,new Rect(x-width/2,y-3,width,24),12,12);Label(dc,value,x,y,11,"#786955",true);}
    public void SavePreview(string path)
    {
        var scene=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);scene.Render(this);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brush(DarkPreview?"#181B22":"#E6E8DF"),null,new Rect(0,0,ActualWidth,ActualHeight));dc.DrawImage(scene,new Rect(0,0,ActualWidth,ActualHeight));}
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
