using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System.Diagnostics;
using System.Text.Json;

namespace Chenpi;

internal static class Program
{
    internal static string[] Args=Array.Empty<string>();
    internal static string? Option(string key){int i=Array.IndexOf(Args,key);return i>=0&&i+1<Args.Length?Args[i+1]:null;}
    [STAThread] public static void Main(string[] args)
    {
        Args=args;
        if(args.Contains("--probe-fullscreen")){Console.WriteLine(MacNative.FullscreenOnPrimary()?"fullscreen":"normal");return;}
        if(!OperatingSystem.IsMacOS()&&!args.Contains("--preview"))throw new PlatformNotSupportedException("Use the Windows build on Windows, or --preview for renderer QA.");
        var rendering=args.Contains("--profile-opengl")?new[]{AvaloniaNativeRenderingMode.OpenGl,AvaloniaNativeRenderingMode.Software}:new[]{AvaloniaNativeRenderingMode.Metal,AvaloniaNativeRenderingMode.OpenGl,AvaloniaNativeRenderingMode.Software};
        AppBuilder.Configure<MacApp>().UsePlatformDetect().With(new AvaloniaNativePlatformOptions{RenderingMode=rendering}).With(new MacOSPlatformOptions{ShowInDock=false}).LogToTrace().StartWithClassicDesktopLifetime(args,ShutdownMode.OnExplicitShutdown);
    }
}
internal sealed class MacApp : Application
{
    private FileStream? singleInstance;
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string data=Program.Option("--data-dir")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Application Support","Chenpi");
            Directory.CreateDirectory(data);
            try{singleInstance=new FileStream(Path.Combine(data,"instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException){desktop.Shutdown();return;}
            var store=new Store(data);
            Dispatcher.UIThread.UnhandledException+=(_,e)=>{store.Log("UI",e.Exception);e.Handled=true;desktop.Shutdown(1);};
            try{desktop.MainWindow=new PetWindow(store,desktop);desktop.MainWindow.Show();}
            catch(Exception e){store.Log("startup",e);desktop.Shutdown(1);}
            desktop.Exit+=(_,_)=>singleInstance?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
internal sealed class PetWindow : Window
{
    private readonly Store store;
    private readonly BehaviorSettingsStore settingsStore;
    private readonly PetEngine engine;
    private readonly Scene scene;
    private readonly MacVoice voice;
    private readonly MacAnimationActivity activity=new();
    private readonly IClassicDesktopStyleApplicationLifetime lifetime;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly List<double> intervals=new();
    private readonly List<object> slowFrames=new();
    private readonly string boot;
    private double lastFrame,lastCheck,lastSave,awake;
    private bool hidden,fullscreen,quitting,startupPending=true;
    private Window? settings,editor;
    private TrayIcon? tray;
    private Point previewPointer=new(-100,-100);
    private bool Preview=>Program.Args.Contains("--preview");
    private bool Audible=>!hidden&&!fullscreen&&!engine.State.Muted&&!engine.Holding&&engine.Action is not("drag" or "land");
    public PetWindow(Store store,IClassicDesktopStyleApplicationLifetime lifetime)
    {
        this.store=store;this.lifetime=lifetime;settingsStore=new(store.DirectoryPath);
        var state=store.Load();engine=new(state,settings:settingsStore.Load());
        boot=MacNative.BootId();awake=MacNative.AwakeSeconds;var gap=ClockMath.Elapsed(state.BootId,state.AwakeSeconds,boot,awake);
        engine.AdvanceNeeds(gap.Seconds);state.ClockGap|=gap.Gap;state.BootId=boot;state.AwakeSeconds=awake;
        Title="陈皮 · 桌面小猫";SystemDecorations=SystemDecorations.None;CanResize=false;ShowInTaskbar=false;ShowActivated=false;Focusable=false;
        TransparencyLevelHint=new[]{WindowTransparencyLevel.Transparent};Background=Brushes.Transparent;
        if(Preview){Title="陈皮 · macOS 渲染预览";SystemDecorations=SystemDecorations.Full;Width=1000;Height=620;ShowInTaskbar=true;Background=new SolidColorBrush(Color.Parse("#181B22"));}
        scene=new(engine){OpenSettings=ShowSettings,SaveNow=Save};Content=scene;
        voice=new(Path.Combine(RuntimeAssets.Root,"audio/cat"));scene.FramePresented+=f=>voice.Present(f,engine.ActionRevision,Audible,engine.State.Volume);
        engine.Sleep();engine.VisualRestorePose?.Invoke("C");
        PointerMoved+=(_,e)=>previewPointer=e.GetPosition(scene);
        Opened+=(_,_)=>
        {
            if(!Preview)FitScreen();scene.LayoutWorld();
            if(Program.Args.Contains("--native-audit")){MacNative.VerifyWindow(this);File.WriteAllText(Path.Combine(store.DirectoryPath,"native-audit.txt"),"Layer roundtrip, click-through roundtrip, nonactivation and pointer read-back passed");}
            MacNative.Configure(this,engine.State.Floating,false);
            activity.SetActive(!Program.Args.Contains("--profile-allow-nap"));
            CreateTray();lastFrame=clock.Elapsed.TotalSeconds;timer.Start();RequestAnimationFrame(AnimationFrame);
            if(Program.Args.Contains("--settings"))ShowSettings();
            if(Program.Option("--editor-audit") is string editorOutput)
            {
                var audit=new BehaviorEditorWindow(engine,settingsStore,Save,Command);editor=audit;audit.Closed+=(_,_)=>editor=null;
                audit.Opened+=(_,_)=>Dispatcher.UIThread.Post(()=>audit.RunFixture(editorOutput),DispatcherPriority.Loaded);audit.Show();
            }
        };
        timer.Tick+=(_,_)=>Maintenance();
        Closing+=(_,e)=>{if(!quitting){e.Cancel=true;hidden=true;}};
        Screens.Changed+=(_,_)=>{if(!Preview){scene.CancelDrag();FitScreen();scene.LayoutWorld();}};
    }
    private void FitScreen()
    {
        var screen=Screens.Primary??throw new InvalidOperationException("No screen");
        Position=screen.WorkingArea.Position;Width=screen.WorkingArea.Width/screen.Scaling;Height=screen.WorkingArea.Height/screen.Scaling;
    }
    private void CreateTray()
    {
        var menu=new NativeMenu();
        void Item(string label,Action action){var item=new NativeMenuItem(label);item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("召回猫猫",()=>Command(engine.Recall));Item("显示 / 隐藏",()=>hidden=!hidden);Item("退出陈皮",Quit);
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(RuntimeAssets.Root,"pets/bluecat/manifest.json")));
        string first=doc.RootElement.GetProperty("animations").GetProperty("video-01")[0].GetString()!;
        using var icon=FrameBitmap.Load(Path.Combine(RuntimeAssets.Root,"pets/bluecat",first),false,false);
        tray=new TrayIcon{ToolTipText="陈皮 · 桌面小猫",Menu=menu,Icon=new WindowIcon(icon.Image),IsVisible=true};
        tray.Clicked+=(_,_)=>hidden=false;TrayIcon.SetIcons(Application.Current!,new TrayIcons{tray});
    }
    private void AnimationFrame(TimeSpan timestamp)
    {
        if(quitting)return;
        double now=clock.Elapsed.TotalSeconds,dt=now-lastFrame;lastFrame=now;
        if(intervals.Count<36000)intervals.Add(dt*1000);
        if(dt>.04&&slowFrames.Count<100)slowFrames.Add(new{Time=now,Milliseconds=dt*1000,engine.Action,scene.DisplayedClip,scene.DisplayedFrame});
        engine.PresentationPaused=!scene.IsVisible;
        if(startupPending&&!engine.PresentationPaused){startupPending=false;engine.BeginStartup(store.IsFirstRun,Program.Args.Contains("--autostart"),boot,Preview);}
        var pointer=OperatingSystem.IsMacOS()?MacNative.Pointer(this):previewPointer;
        MacNative.PassThrough(this,!scene.IsVisible||!scene.AcceptsPointer(pointer));
        if(scene.IsVisible)scene.InputTick(Math.Min(.12,dt),pointer);else engine.ObservePointer(0,false,new());
        engine.Update(Math.Min(.12,dt),DateTime.Now.Hour);if(!Audible)voice.Silence();
        RequestAnimationFrame(AnimationFrame);
    }
    private void Maintenance()
    {
        if(quitting)return;double now=clock.Elapsed.TotalSeconds;
        double nextAwake=MacNative.AwakeSeconds;engine.AdvanceNeeds(Math.Max(0,nextAwake-awake));awake=nextAwake;engine.State.AwakeSeconds=awake;
        if(now-lastCheck>=.5)
        {
            lastCheck=now;fullscreen=!Preview&&MacNative.FullscreenOnPrimary();
            if((fullscreen||hidden)&&scene.IsInteracting)scene.CancelDrag();
            scene.IsVisible=!fullscreen&&!hidden;MacNative.Configure(this,engine.State.Floating,fullscreen||hidden);
            activity.SetActive(scene.IsVisible&&WindowState!=WindowState.Minimized&&MacNative.WindowVisible(this)&&!Program.Args.Contains("--profile-allow-nap"));
        }
        if(!Audible)voice.Silence();
        if(!scene.IsInteracting&&now-lastSave>5){Save();lastSave=now;}
        if(double.TryParse(Program.Option("--exit-after"),out double end)&&now>=end){WriteDiagnostics();Quit();}
    }
    private void WriteDiagnostics()
    {
        if(intervals.Count==0)throw new InvalidOperationException("No animation frames were presented");
        string path=Program.Option("--snapshot")??Path.Combine(store.DirectoryPath,"preview.png");
        using var bitmap=new RenderTargetBitmap(new PixelSize((int)Bounds.Width,(int)Bounds.Height),new Vector(96,96));bitmap.Render(scene);bitmap.Save(path);
        var sorted=intervals.Order().ToArray();
        File.WriteAllText(path+".json",JsonSerializer.Serialize(new{Runtime=RuntimeInformation(),engine.Action,engine.StartupActive,scene.DisplayedClip,scene.DisplayedFrame,scene.CachedFrameBytes,scene.CacheMisses,scene.MaxDecodeMilliseconds,WorkingSet=Process.GetCurrentProcess().WorkingSet64,CpuMilliseconds=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds,ElapsedSeconds=clock.Elapsed.TotalSeconds,Frames=intervals.Count,P95Milliseconds=sorted[(int)(sorted.Length*.95)],MaxMilliseconds=sorted[^1],slowFrames,scene.ClipTransitions}));
    }
    private static string RuntimeInformation()=>System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    private void Save()=>store.QueueSave(engine.State);
    private void Command(Action action){scene.CancelDrag();hidden=false;action();Save();}
    private async void Quit(){if(quitting)return;if(editor is BehaviorEditorWindow workbench&&!await workbench.RequestClose())return;if(quitting)return;quitting=true;timer.Stop();activity.Dispose();voice.Dispose();scene.Dispose();tray?.Dispose();Save();store.Flush();settings?.Close();Close();lifetime.Shutdown();}
    private static Button Button(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(0,0,8,10)};b.Click+=(_,_)=>action();return b;}
    private static TextBlock Text(string value,double size=14)=>new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    private CheckBox Toggle(string label,bool value,Action<bool> action){var c=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(0,0,0,10)};c.IsCheckedChanged+=(_,_)=>action(c.IsChecked==true);return c;}
    private void ShowSettings()
    {
        if(settings is not null){settings.Activate();return;}
        var w=new Window{Title="陈皮 · 小猫的家",Width=490,Height=760,MinWidth=430,MinHeight=580};settings=w;
        var body=new StackPanel{Margin=new Thickness(28)};body.Children.Add(Text("陈皮的小日子",28));body.Children.Add(Text("一只住在桌面上的小猫。\n不用打卡，也没有惩罚，记得偶尔摸摸它。"));
        var status=Text(store.Warning??store.WriteError??"");body.Children.Add(status);
        body.Children.Add(Button("行为工作台 · 流程图与参数",()=>{if(editor is not null){editor.Activate();return;}editor=new BehaviorEditorWindow(engine,settingsStore,Save,Command);editor.Closed+=(_,_)=>editor=null;editor.Show();}));
        body.Children.Add(Toggle("悬浮在普通窗口上（全屏时隐藏）",engine.State.Floating,on=>{engine.State.Floating=on;MacNative.Configure(this,on,hidden||fullscreen);Save();}));
        if(OperatingSystem.IsMacOS())body.Children.Add(Toggle("开机显示小猫",AutoStart.Enabled,on=>{try{AutoStart.Set(on);status.Text="自启设置已保存";}catch(Exception e){status.Text=e.Message;}}));
        body.Children.Add(Toggle("静音（保留动作提醒）",engine.State.Muted,on=>{engine.State.Muted=on;if(on)voice.Silence();Save();}));
        body.Children.Add(Text("小猫与物件大小",12));var size=new Slider{Minimum=.7,Maximum=1.4,Value=engine.State.Scale,TickFrequency=.1,IsSnapToTickEnabled=true};size.ValueChanged+=(_,_)=>{engine.State.Scale=size.Value;scene.LayoutWorld();Save();};body.Children.Add(size);
        body.Children.Add(Text("叫声音量",12));var volume=new Slider{Minimum=0,Maximum=1,Value=engine.State.Volume};volume.ValueChanged+=(_,_)=>{engine.State.Volume=volume.Value;voice.SetVolume(volume.Value);Save();};body.Children.Add(volume);
        var controls=new WrapPanel();controls.Children.Add(Button("召回猫猫",()=>Command(engine.Recall)));controls.Children.Add(Button("恢复摆放",()=>Command(()=>{engine.Layout(scene.WorldWidth,scene.WorldHeight,true);engine.Recall();})));controls.Children.Add(Button("显示 / 隐藏",()=>hidden=!hidden));body.Children.Add(controls);
        body.Children.Add(Text("体验动作"));var demo=new WrapPanel();foreach(var (label,action) in new[]{("吃饭","eat"),("喝水","drink"),("猫砂盆","toilet"),("回窝","sleep")})demo.Children.Add(Button(label,()=>Command(()=>engine.Demo(action))));body.Children.Add(demo);
        body.Children.Add(Text("点食水盆补给，点猫砂盆清理。\n拖动物品可以搬家；长按小猫拎起，拖进窝里松手睡觉。\n点羽毛棒拿起，再点一次归位。\n仅猫窝右键打开设置。",12));body.Children.Add(Button("退出陈皮",Quit));
        w.Content=new ScrollViewer{Content=body};w.Closed+=(_,_)=>settings=null;w.Show();
    }
}
