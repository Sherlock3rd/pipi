using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using System.Text.Json;

namespace Chenpi;

internal sealed partial class Scene : Panel,IDisposable
{
    private sealed class Layer : Control
    {
        private readonly Scene scene;private readonly bool overlay;
        public Layer(Scene scene,bool overlay){this.scene=scene;this.overlay=overlay;IsHitTestVisible=false;}
        public override void Render(DrawingContext context)
        {long start=Stopwatch.GetTimestamp();using var dc=new DrawingContextAdapter(context);if(overlay)scene.RenderOverlay(dc);else scene.RenderArtwork(dc);if(!overlay)scene.RecordRender(Stopwatch.GetElapsedTime(start).TotalMilliseconds);}
    }
    private readonly Layer art,ui;
    private readonly DropShadowEffect shadow=new(){Color=Colors.Black,Opacity=.38,BlurRadius=14,OffsetY=3,OffsetX=0};
    public PetEngine Engine {get;}
    public Action? OpenSettings,SaveNow;
    internal event Action<SpriteFrame?>? FramePresented;
    private readonly FrameCache<FrameBitmap> cache;
    private readonly Dictionary<string,IReadOnlyList<FrameBitmap>> frames=new();
    private readonly Dictionary<string,List<string>> framePaths=new();
    private readonly WeakMetadata<FrameBitmap,Rect> frameBounds=new();
    private readonly AnimationVariants variants=new();
    private readonly SpritePlayback playback=new();
    private static readonly Dictionary<string,SolidColorBrush> brushes=new();
    private static readonly Dictionary<string,Geometry> shapes=new();
    private static readonly Dictionary<(string,double,string),FormattedText> labels=new();
    private static FrameBitmap Prop(string name)=>FrameBitmap.Load(Path.Combine(RuntimeAssets.Root,"props",name+".png"),name is "nest" or "litter-tray" or "kibble",true,name=="kibble"?128:8,true,640);
    private static readonly FrameBitmap foodBowl=Prop("food-bowl-empty"),waterCup=Prop("water-cup-empty"),kibble=Prop("kibble"),nestSprite=Prop("nest"),litterSprite=Prop("litter-tray");
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private bool pressed,wandHeld,nestOcclusion;
    public bool IsDragging {get;private set;}
    public bool IsInteracting=>pressed||wandHeld;
    private double pressedAt,lift,poseLift,shownFood,shownWater;
    private Point pointer,pressedPoint;
    private Vector grabOffset;
    private Spot originalPosition;
    private string pressedObject="cat",lastSpriteClip="";
    private IPointer? captured;
    private Rect? currentCatBounds;
    private long spriteRevision=-1;
    private AnimationVariant? selectedVariant;
    private double fps=24;
    public const double BowlScale=.60;
    public bool PreviewSupplies {get;set;}
    public bool PreviewRightWalk {get;set;}
    public string DisplayedClip {get;private set;}="";
    public int DisplayedFrame {get;private set;}
    public SpriteClip? DisplayedDefinition {get;private set;}
    public List<object> ClipTransitions {get;}=new();
    public long CachedFrameBytes=>cache.Bytes;
    public long CacheMisses=>cache.Misses;
    public double MaxDecodeMilliseconds=>cache.MaxLoadMilliseconds;
    private readonly List<double> renderTimes=new();
    private int uiDecodes;
    private double uiDecodeMilliseconds;
    private void RecordRender(double ms){if(renderTimes.Count<36000)renderTimes.Add(ms);}
    public object RenderingDiagnostics=>new{Count=renderTimes.Count,P95=renderTimes.Count==0?0:renderTimes.Order().ElementAt((int)(renderTimes.Count*.95)),Max=renderTimes.Count==0?0:renderTimes.Max(),UiDecodes=uiDecodes,UiDecodeMilliseconds=uiDecodeMilliseconds};
    public double Scale=>Engine.State.Scale;
    public double WorldWidth=>Bounds.Width/Scale;
    public double WorldHeight=>Bounds.Height/Scale;
    public Scene(PetEngine engine)
    {
        Engine=engine;shownFood=engine.State.Food;shownWater=engine.State.Water;
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(RuntimeAssets.Root,"pets/bluecat/manifest.json")));
        var manifest=doc.RootElement;bool prepared=manifest.GetProperty("videoMattePrepared").GetBoolean();
        string root=Path.GetFullPath(Path.Combine(RuntimeAssets.Root,"pets/bluecat"))+Path.DirectorySeparatorChar;
        var videoPaths=manifest.GetProperty("animations").EnumerateObject().Where(p=>p.Name.StartsWith("video-",StringComparison.Ordinal)).SelectMany(p=>p.Value.EnumerateArray().Select(v=>Path.GetFullPath(Path.Combine(root,v.GetString()!)))).ToHashSet(StringComparer.Ordinal);
        cache=new(96L*1024*1024,path=>{bool video=videoPaths.Contains(path);long start=Stopwatch.GetTimestamp();var image=FrameBitmap.Load(path,video,video&&!prepared);if(Dispatcher.UIThread.CheckAccess()){uiDecodes++;uiDecodeMilliseconds+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}frameBounds[image]=image.Extent;return (image,image.Bytes);},image=>Dispatcher.UIThread.Post(image.Dispose,DispatcherPriority.Background));
        foreach(var clip in manifest.GetProperty("animations").EnumerateObject())
        {
            var paths=clip.Value.EnumerateArray().Select(v=>Path.GetFullPath(Path.Combine(root,v.GetString()!))).ToList();
            if(paths.Any(p=>!p.StartsWith(root,StringComparison.Ordinal)||!RuntimeAssets.Exists(p)))throw new InvalidDataException("Missing/invalid animation: "+clip.Name);
            framePaths[clip.Name]=paths;frames[clip.Name]=new FrameSequence<FrameBitmap>(paths,cache);
        }
        variants.Load(manifest,frames.ContainsKey);playback.Load(manifest,id=>frames.TryGetValue(id,out var f)?f.Count:0);
        if(manifest.TryGetProperty("wallContactOffsets",out var wall)){engine.WallRightOffset=wall.GetProperty("right").GetDouble();engine.WallLeftOffset=wall.GetProperty("left").GetDouble();}
        if(manifest.TryGetProperty("relaxedHalfWidth",out var footprint))engine.RelaxedHalfWidth=footprint.GetDouble();
        Engine.VisualVelocity=playback.HorizontalVelocity;Engine.VisualActionDuration=playback.ActionDuration;
        Engine.VisualCareReady=playback.PrepareCare;Engine.VisualRequestFinishReady=playback.PrepareRequestFinish;
        Engine.VisualStandReady=playback.PrepareStand;playback.WalkingCallEvery=()=>(int)Engine.Settings.Get("walk.callEvery");
        Engine.VisualTravel=playback.TravelTo;Engine.VisualBurialWindow=playback.BurialWindow;Engine.VisualConsumptionWindow=playback.ConsumptionWindow;
        Engine.ExpressionsEnabled=playback.HasExpressions;Engine.VisualPoseReady=playback.PreparePose;Engine.VisualWallRestComplete=playback.WallRestComplete;
        Engine.CompletionEnabled=playback.HasCompletion;Engine.VisualRestorePose=playback.RestorePose;Engine.VisualDropPose=()=>playback.DropPose;
        Engine.RestoreRelaxedSleep();if(Engine.Action.StartsWith("rest-"))playback.RestorePose(Engine.RelaxedPose);
        foreach(string id in new[]{"video-01","video-20","video-32"})if(frames.TryGetValue(id,out var f))_ = f[0];
        art=new(this,false){Effect=shadow};
        if(Program.Args.Contains("--profile-no-shadow"))art.Effect=null;
        ui=new(this,true);Children.Add(art);Children.Add(ui);Background=Brushes.Transparent;
        RenderOptions.SetBitmapInterpolationMode(this,Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        SizeChanged+=(_,_)=>LayoutWorld();
    }
    private IReadOnlyList<FrameBitmap>? GetFrames(string id)=>frames.GetValueOrDefault(id);
    private double lastPrefetch=-1;
    private void PrefetchPlayback(string action)
    {
        if(Engine.Now-lastPrefetch<.12)return;lastPrefetch=Engine.Now;
        cache.Prefetch(playback.PeekFrames(action,Engine.Now,Engine.FacingLeft).Where(f=>framePaths.ContainsKey(f.Clip)).Select(f=>framePaths[f.Clip][f.Index]).Distinct().ToArray());
    }
    public void LayoutWorld(){if(WorldWidth>=400&&WorldHeight>=250)Engine.Layout(WorldWidth,WorldHeight);Repaint();}
    public void Repaint(){art.InvalidateVisual();ui.InvalidateVisual();}
    public void InputTick(double dt,Point at)
    {
        pointer=World(at);
        if(pressed&&!IsDragging&&clock.Elapsed.TotalSeconds-pressedAt>=Engine.Settings.Get("drag.hold"))StartDrag();
        double target=!Engine.Grounded&&pressed&&pressedObject=="cat"?(IsDragging?8:Math.Min(1,(clock.Elapsed.TotalSeconds-pressedAt)/Engine.Settings.Get("drag.hold"))*3):0;
        lift+=(target-lift)*(1-Math.Exp(-dt*24));MoveDragged(pointer);
        if(wandHeld)Engine.SetToy(true,new(Math.Clamp(pointer.X,0,WorldWidth),Math.Clamp(pointer.Y,0,WorldHeight)));
        Engine.ObservePointer(dt,IsVisible&&!pressed&&CatRect.Contains(pointer),new(pointer.X,pointer.Y));
        shownFood+=Math.Clamp(Engine.State.Food-shownFood,-dt*95,dt*95);shownWater+=Math.Clamp(Engine.State.Water-shownWater,-dt*95,dt*95);
        if(shadow.BlurRadius!=14*Scale){shadow.BlurRadius=14*Scale;shadow.OffsetY=3*Scale;}Repaint();
    }
    private Point World(Point p)=>new(p.X/Scale,p.Y/Scale);
    private Rect CatRect=>currentCatBounds is Rect b?b.Translate(new Vector(Engine.VisualPosition.X,Engine.VisualPosition.Y-lift-poseLift)).Inflate(7):new(Engine.State.X-85,Engine.State.Y-158,170,170);
    private Rect NestRect=>new(Engine.Nest.X-InteractionGeometry.NestHalfWidth,Engine.Nest.Y-InteractionGeometry.NestHeight,InteractionGeometry.NestWidth,InteractionGeometry.NestHeight);
    private Rect LitterRect=>new(Engine.LitterSpot.X-InteractionGeometry.LitterHalfWidth,Engine.LitterSpot.Y-InteractionGeometry.LitterHeight,InteractionGeometry.LitterHalfWidth*2,InteractionGeometry.LitterHeight);
    private Rect ObjectRect(Spot p)=>new(p.X-36,p.Y+InteractionGeometry.BowlBaseOffset(p==Engine.WaterSpot)-70,72,70);
    private Rect WandRect=>new(Engine.WandHome.X-39,Engine.WandHome.Y-38,78,73);
    private bool AtNest=>Engine.CanDropInNest(new(pointer.X,pointer.Y));
    public bool AcceptsPointer(Point p){p=World(p);return IsInteracting||CatRect.Contains(p)||NestRect.Contains(p)||LitterRect.Contains(p)||ObjectRect(Engine.FoodSpot).Contains(p)||ObjectRect(Engine.WaterSpot).Contains(p)||WandRect.Contains(p);}
    private bool OpensSettingsAt(Point p)=>!IsInteracting&&!CatRect.Contains(p)&&!WandRect.Contains(p)&&!LitterRect.Contains(p)&&!ObjectRect(Engine.FoodSpot).Contains(p)&&!ObjectRect(Engine.WaterSpot).Contains(p)&&NestRect.Contains(p);
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);var point=e.GetCurrentPoint(this);if(!point.Properties.IsLeftButtonPressed)return;
        var p=World(point.Position);pointer=p;
        if(wandHeld){CancelWand();e.Handled=true;return;}
        if(WandRect.Contains(p)){wandHeld=true;Engine.SetToy(true,new(p.X,p.Y));captured=e.Pointer;captured.Capture(this);e.Handled=true;return;}
        string? hit=FrontBowlContains(p,true)?"water":FrontBowlContains(p,false)?"food":CatRect.Contains(p)?"cat":LitterRect.Contains(p)?"litter":ObjectRect(Engine.WaterSpot).Contains(p)?"water":ObjectRect(Engine.FoodSpot).Contains(p)?"food":NestRect.Contains(p)?"nest":null;
        if(hit is null)return;pressedObject=hit;originalPosition=hit=="cat"?Engine.VisualPosition:Engine.ObjectPosition(hit);
        pressed=true;pressedAt=clock.Elapsed.TotalSeconds;pressedPoint=p;grabOffset=new(p.X-originalPosition.X,p.Y-originalPosition.Y);Engine.Holding=true;captured=e.Pointer;captured.Capture(this);e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p=World(e.GetPosition(this));pointer=p;
        if(pressed&&!IsDragging&&(((Vector)(p-pressedPoint)).Length>=6||clock.Elapsed.TotalSeconds-pressedAt>=Engine.Settings.Get("drag.hold")))StartDrag();MoveDragged(p);
    }
    private void StartDrag(){IsDragging=true;if(pressedObject=="cat")Engine.BeginDrag();Cursor=new(StandardCursorType.Hand);MoveDragged(pointer);}
    private void MoveDragged(Point p){if(!IsDragging)return;if(pressedObject=="cat")Engine.DragTo(new(p.X-grabOffset.X,p.Y-grabOffset.Y));else Engine.MoveObject(pressedObject,new(p.X-grabOffset.X,p.Y-grabOffset.Y));}
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        pointer=World(e.GetPosition(this));
        if(e.InitialPressMouseButton==MouseButton.Right){if(OpensSettingsAt(pointer))OpenSettings?.Invoke();e.Handled=true;return;}
        if(!pressed)return;MoveDragged(pointer);bool dragged=IsDragging;pressed=false;IsDragging=false;Engine.Holding=false;captured?.Capture(null);captured=null;Cursor=Cursor.Default;
        if(dragged){if(pressedObject=="cat")Engine.Drop(AtNest);}
        else if(((Vector)(pointer-pressedPoint)).Length<12){if(pressedObject=="cat")Engine.Interact();else if(pressedObject!="nest")Engine.Refill(pressedObject);}
        SaveNow?.Invoke();e.Handled=true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){if(IsInteracting)CancelDrag();base.OnPointerCaptureLost(e);}
    private void CancelWand(){wandHeld=false;Engine.SetToy(false,new());captured?.Capture(null);captured=null;SaveNow?.Invoke();}
    public void CancelDrag()
    {
        bool dragged=IsDragging;pressed=false;IsDragging=false;Engine.Holding=false;wandHeld=false;Engine.SetToy(false,new());
        if(dragged){if(pressedObject=="cat"){Engine.State.X=originalPosition.X;Engine.State.Y=originalPosition.Y;Engine.Drop(false);}else Engine.MoveObject(pressedObject,originalPosition);}
        captured?.Capture(null);captured=null;Cursor=Cursor.Default;SaveNow?.Invoke();
    }
    public void Dispose()=>cache.Dispose();
}
