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

namespace Chenpi;

internal sealed class Scene : FrameworkElement
{
    public PetEngine Engine {get;}
    public Action? OpenSettings;
    public Action? SaveNow;
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
    private double poseUpdatedAt=-1;
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
    public List<object> ClipTransitions {get;}=new();
    public bool PreviewRightWalk {get;set;}
    public bool PreviewSupplies {get;set;}
    private long spriteRevision=-1;
    private AnimationVariant? selectedVariant;
    private double fps=8;
    public double Scale=>Engine.State.Scale;
    public double WorldWidth=>ActualWidth/Scale;
    public double WorldHeight=>ActualHeight/Scale;
    public Scene(PetEngine engine)
    {
        Engine=engine;shownFood=engine.State.Food;shownWater=engine.State.Water;Focusable=false;Cursor=Cursors.Arrow;
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        LoadSprites();
        Engine.CanAdvanceMovement=null;
        Engine.VisualVelocity=playback.HorizontalVelocity;
        Engine.VisualActionDuration=playback.ActionDuration;
        Engine.VisualCareReady=playback.PrepareCare;
        Engine.VisualConsumptionWindow=playback.ConsumptionWindow;
        // Prepare the first pickup pose before input, including decoded sprites and drawing caches.
        var warm=new DrawingGroup();using(var drawing=warm.Open())DrawCat(drawing,0,0,"drag",0,false);
        playback.Reset();
        SizeChanged+=(_,_)=>LayoutWorld();
        LostMouseCapture+=(_,_)=>{if(pressed||IsDragging||wandHeld)CancelDrag();};
    }
    private void LoadSprites()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"assets","pets","bluecat","manifest.json");
        if(!File.Exists(path))return;
        try
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(path));
            fps=Math.Clamp(doc.RootElement.GetProperty("fps").GetDouble(),1,30);
            string root=Path.GetFullPath(Path.GetDirectoryName(path)!)+Path.DirectorySeparatorChar;
            foreach(var property in doc.RootElement.GetProperty("animations").EnumerateObject())
            {
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
            bool prepared=doc.RootElement.TryGetProperty("videoMattePrepared",out var readyMatte)&&readyMatte.GetBoolean();
            // Decode before showing the window. Switching a clip does zero disk IO/decode.
            Parallel.ForEach(framePaths.Where(p=>p.Key.StartsWith("video-",StringComparison.Ordinal)),
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
        if(pressed&&!IsDragging&&watch.Elapsed.TotalSeconds-pressedAt>=HoldSeconds)
        {StartDrag();}
        double liftTarget=!Engine.Grounded&&pressed&&pressedObject=="cat"?(IsDragging?8:Math.Min(1,(watch.Elapsed.TotalSeconds-pressedAt)/HoldSeconds)*3):0;
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
    private Rect NestRect=>new(Engine.Nest.X-98,Engine.Nest.Y-146,196,146);
    private Rect LitterRect=>new(Engine.LitterSpot.X-InteractionGeometry.LitterHalfWidth,Engine.LitterSpot.Y-InteractionGeometry.LitterHeight,InteractionGeometry.LitterHalfWidth*2,InteractionGeometry.LitterHeight);
    private Rect ObjectRect(Spot p,double w=92)=>p==Engine.FoodSpot||p==Engine.WaterSpot
        ?new(p.X-30,p.Y-70,60,70):new(p.X-w/2,p.Y-62,w,62);
    private Rect SettingsRect=>new(Engine.Nest.X+70,Engine.Nest.Y-150,30,30);
    private Rect WandRect=>new(Engine.WandHome.X-39,Engine.WandHome.Y-38,78,73);
    private bool AtNest=>Engine.CanDropInNest(new Spot(pointer.X,pointer.Y));
    protected override HitTestResult? HitTestCore(PointHitTestParameters p)
    {
        var at=World(p.HitPoint);
        return pressed||wandHeld||WandRect.Contains(at)||CatRect.Contains(at)||NestRect.Contains(at)||SettingsRect.Contains(at)||ObjectRect(Engine.FoodSpot).Contains(at)||ObjectRect(Engine.WaterSpot).Contains(at)||LitterRect.Contains(at)
            ?new PointHitTestResult(this,p.HitPoint):null;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p=World(e.GetPosition(this));
        if(wandHeld){ReturnWand();e.Handled=true;return;}
        if(SettingsRect.Contains(p)){OpenSettings?.Invoke();e.Handled=true;return;}
        if(WandRect.Contains(p))
        {wandHeld=true;Engine.SetToy(true,new Spot(p.X,p.Y));CaptureMouse();Cursor=Cursors.Cross;InvalidateVisual();e.Handled=true;return;}
        string? hit=CatRect.Contains(p)?"cat":LitterRect.Contains(p)?"litter":ObjectRect(Engine.WaterSpot).Contains(p)?"water":ObjectRect(Engine.FoodSpot).Contains(p)?"food":NestRect.Contains(p)?"nest":null;
        if(hit is null)return;
        pressedObject=hit;originalPosition=hit=="cat"?Engine.VisualPosition:Engine.ObjectPosition(hit);
        pressed=true;pressedAt=watch.Elapsed.TotalSeconds;pressedPoint=p;grabOffset=new Vector(p.X-originalPosition.X,p.Y-originalPosition.Y);Engine.Holding=true;CaptureMouse();e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p=World(e.GetPosition(this));
        if(pressed&&!IsDragging&&((p-pressedPoint).Length>=6||watch.Elapsed.TotalSeconds-pressedAt>=HoldSeconds))StartDrag();
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
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e){if(wandHeld)ReturnWand();else OpenSettings?.Invoke();e.Handled=true;}
    protected override void OnRender(DrawingContext dc)
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        base.OnRender(dc);dc.PushTransform(new ScaleTransform(Scale,Scale));
        if(PreviewSupplies)
        {
            for(int n=0;n<=5;n++)
            {
                double x=80+n*150;
                DrawBowl(dc,new Spot(x,140),n*20,false,0);
                DrawBowl(dc,new Spot(x,250),n*20,true,0);
                DrawLitterLevel(dc,new Spot(x,360),n*20);
            }
            dc.Pop();return;
        }
        if(PreviewRightWalk)
        {
            DrawCat(dc,100+(Engine.Now*45)%Math.Max(1,WorldWidth-200),WorldHeight*.66,"walk",Engine.Now,false);
            dc.Pop();return;
        }
        DrawNest(dc,false);
        DrawBowl(dc,Engine.FoodSpot,Engine.State.Food,false,Engine.Now);
        DrawBowl(dc,Engine.WaterSpot,Engine.State.Water,true,Engine.Now);
        DrawLitter(dc);
        if(Engine.State.Sleeping)nestOcclusion=Engine.State.SleepingInNest;
        DrawCat(dc,Engine.VisualPosition.X,Engine.VisualPosition.Y-lift,Engine.AligningForCare?"care-ready":Engine.Action,Engine.ActionTime,Engine.FacingLeft);
        if(!Engine.State.Sleeping&&DisplayedClip is not ("video-20" or "video-21" or "video-18"))nestOcclusion=false;
        if(nestOcclusion)DrawNest(dc,true);
        if(Engine.Action is "toilet" or "bury")
        {
            var p=Engine.LitterSpot;
            // Only the tray's front wall occludes paws; the back rim stays behind.
            dc.PushClip(new RectangleGeometry(new Rect(p.X-InteractionGeometry.LitterHalfWidth,p.Y-36,InteractionGeometry.LitterHalfWidth*2,36)));
            DrawLitter(dc);dc.Pop();
        }
        if(Engine.Action is "eat" or "drink")
        {
            bool water=Engine.Action=="drink";var p=water?Engine.WaterSpot:Engine.FoodSpot;
            // The muzzle enters the opening, behind the near lip of the bowl.
            dc.PushClip(new RectangleGeometry(new Rect(p.X-36,p.Y-30,72,30)));
            DrawBowl(dc,p,water?shownWater:shownFood,water,Engine.Now);dc.Pop();
        }
        DrawWand(dc);
        if(IsDragging&&pressedObject=="cat"&&AtNest)
        {dc.DrawRoundedRectangle(null,new Pen(Brush("#A3C7A2"),3),NestRect,30,30);}
        var age=DateTimeOffset.UtcNow-Engine.State.AdoptedAt;
        string span=age.TotalDays>=1?$"相伴 {Math.Max(0,(int)age.TotalDays)} 天":$"相伴 {Math.Max(0,(int)age.TotalHours):00}:{Math.Max(0,age.Minutes):00}";
        LabelPill(dc,span,Engine.Nest.X,Engine.Nest.Y+12,102);
        dc.DrawEllipse(Brush("#F4EEE4"),new Pen(Brush("#CABDAC"),1),new Point(SettingsRect.X+15,SettingsRect.Y+15),14,14);
        Label(dc,"···",SettingsRect.X+15,SettingsRect.Y+1,19,"#746658",true);
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
        if(nestSprite is not null)
        {
            if(foreground)dc.PushClip(System.Windows.Media.Geometry.Parse(FormattableString.Invariant($"M {p.X-98},{p.Y-84} Q {p.X},{p.Y-16} {p.X+98},{p.Y-84} L {p.X+98},{p.Y} L {p.X-98},{p.Y} Z")));
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
        // Align the visible base (excluding source-image transparent padding).
        p=new Spot(p.X,p.Y-(water?19:22));
        dc.PushTransform(new ScaleTransform(BowlScale,BowlScale,p.X,p.Y));
        DrawBowlFullSize(dc,p,fill,water,time);dc.Pop();
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
        DrawLitterLevel(dc,Engine.LitterSpot,Engine.State.Litter);
    }
    private static void DrawLitterLevel(DrawingContext dc,Spot p,double quantity)
    {
        dc.PushTransform(new ScaleTransform(InteractionGeometry.LitterScale,InteractionGeometry.LitterScale,p.X,p.Y));
        DrawLitterUnscaled(dc,p,quantity);dc.Pop();
    }
    private static void DrawLitterUnscaled(DrawingContext dc,Spot p,double quantity)
    {
        p=new Spot(p.X,p.Y-28);
        if(litterSprite is not null)
        {
            dc.DrawImage(litterSprite,new Rect(p.X-56,p.Y-34,112,62));
            for(int i=0;i<PetEngine.SupplyLayers(quantity);i++)
            {
                double x=p.X-29+i*14,y=p.Y+3+(i%2)*4;
                dc.DrawEllipse(Brush("#80705A"),null,new Point(x,y),6,3.5);
                dc.DrawEllipse(Brush("#9C876C"),null,new Point(x-1,y-.8),4,2);
            }
            return;
        }
        Shadow(dc,p.X,p.Y+26,59,9);
        dc.DrawRoundedRectangle(Brush("#A4AF9C"),new Pen(Brush("#79836F"),1.5),new Rect(p.X-56,p.Y-27,112,56),15,15);
        dc.DrawRoundedRectangle(Brush("#E3D8BE"),new Pen(Brush("#C2B79C"),1),new Rect(p.X-47,p.Y-21,94,32),11,11);
        for(int i=0;i<25;i++)dc.DrawEllipse(Brush("#C5B79B"),null,new Point(p.X-39+(i*17%78),p.Y-15+(i*11%21)),1.5,1);
        for(int i=0;i<PetEngine.SupplyLayers(quantity);i++)dc.DrawEllipse(Brush("#8D7560"),null,new Point(p.X-32+i*16,p.Y-5+(i%2)*6),8,5);
    }
    private void DrawVideoFrame(DrawingContext dc,BitmapSource image,SpriteClip definition)
    {
        var b=frameBounds.TryGetValue(image,out var bounds)?bounds:new Rect(0,0,1,1);
        dc.DrawImage(image,new Rect((b.X-definition.AnchorX)*definition.Width,(b.Y-definition.AnchorY)*definition.Height,b.Width*definition.Width,b.Height*definition.Height));
    }
    private void DrawCat(DrawingContext dc,double x,double y,string action,double time,bool left)
    {
        if(action==Engine.Action&&spriteRevision!=Engine.ActionRevision)
        {spriteRevision=Engine.ActionRevision;selectedVariant=variants.Choose(action);}
        var variant=action==Engine.Action?selectedVariant:null;
        var sample=playback.Sample(action,Engine.Now,left);
        if(variant is null&&sample is SpriteFrame sprite&&GetFrames(sprite.Clip) is {} generated)
        {
            double targetLift=Engine.Grounded?InteractionGeometry.SurfaceLift(sprite.Clip,sprite.Index/(double)Math.Max(1,sprite.Definition.Count-1),nestOcclusion,action):0;
            double delta=poseUpdatedAt<0?0:Math.Max(0,Engine.Now-poseUpdatedAt);
            poseLift=poseUpdatedAt<0?targetLift:poseLift+(targetLift-poseLift)*(1-Math.Exp(-delta*18));poseUpdatedAt=Engine.Now;
            y-=poseLift;
            DisplayedClip=sprite.Clip;DisplayedFrame=sprite.Index;
            var definition=sprite.Definition;
            if(frameBounds.TryGetValue(generated[sprite.Index],out var bounds))
                currentCatBounds=new Rect((bounds.X-definition.AnchorX)*definition.Width,(bounds.Y-definition.AnchorY)*definition.Height,bounds.Width*definition.Width,bounds.Height*definition.Height);
            if(lastSpriteClip!=sprite.Clip)
            {
                lastSpriteClip=sprite.Clip;
                if(ClipTransitions.Count>=100)ClipTransitions.RemoveAt(0);
                ClipTransitions.Add(new {Time=Engine.Now,Clip=sprite.Clip,X=x,Y=y});
            }
            bool mirror=left&&definition.MirrorWithFacing;
            dc.PushTransform(new TranslateTransform(x,y));if(mirror)dc.PushTransform(new ScaleTransform(-1,1));
            // One supplied frame per instant. Layering the opaque previous pose
            // underneath a fading new pose created double outlines and bright flashes.
            DrawVideoFrame(dc,generated[sprite.Index],definition);
            if(mirror)dc.Pop();dc.Pop();return;
        }
        poseLift=0;lastSpriteClip="";currentCatBounds=null;
        DisplayedClip=variant?.Id??action;DisplayedFrame=0;
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
