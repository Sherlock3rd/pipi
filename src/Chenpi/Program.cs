using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace Chenpi;

internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        string? data=Option(args,"--data-dir");
        data??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Chenpi");
        using var single=new Mutex(true,"Local\\Chenpi-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data)))[..16],out bool created);
        if(!created){System.Windows.MessageBox.Show("陈皮已经在运行啦。请在系统托盘找到小猫图标。","陈皮");return;}
        Store? store=null;
        try
        {
            store=new Store(data);var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            app.DispatcherUnhandledException+=(_,e)=>{store.Log("UI",e.Exception);System.Windows.MessageBox.Show("出现错误，已保留存档。\n"+e.Exception.Message,"陈皮");e.Handled=true;app.Shutdown(1);};
            var main=new PetWindow(store,args);app.Run(main);
        }
        catch(Exception e){store?.Log("startup",e);System.Windows.MessageBox.Show(e.Message,"陈皮启动失败");}
    }
    internal static string? Option(string[] args,string key){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
}

internal sealed class PetWindow : Window
{
    private readonly Store store;
    private readonly PetEngine engine;
    private readonly Scene scene;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly Stopwatch animationClock=Stopwatch.StartNew();
    private readonly Forms.NotifyIcon tray;
    private readonly string[] args;
    private double previousAnimation;
    private double previousAwake;
    private double lastSave;
    private double lastWindowCheck=-1;
    private bool hiddenByUser;
    private bool fullScreen;
    private bool attached;
    private bool quitting;
    private bool initialized;
    private Window? settings;
    private TextBlock? layerStatus;
    private string windowStatus="桌面层级";
    private readonly string boot;
    private bool Preview=>args.Contains("--preview");
    private int renderedFrames;
    private double frameIntervals;
    private double maxFrameInterval;

    public PetWindow(Store storage,string[] arguments,PetEngine? existingEngine=null)
    {
        store=storage;args=arguments;
        var state=existingEngine?.State??store.Load();engine=existingEngine??new PetEngine(state);
        boot=Native.BootIdentifier();previousAwake=Native.AwakeSeconds;
        var elapsed=ClockMath.Elapsed(state.BootId,state.AwakeSeconds,boot,previousAwake);
        engine.AdvanceNeeds(elapsed.Seconds);state.ClockGap|=elapsed.Gap;state.BootId=boot;state.AwakeSeconds=previousAwake;
        Title="陈皮 · 桌面小猫";WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;
        if(Preview){Title="陈皮 · 交互预览";AllowsTransparency=false;WindowStyle=WindowStyle.SingleBorderWindow;Background=Color("#E6E8DF");ShowInTaskbar=true;ShowActivated=true;Width=820;Height=440;WindowStartupLocation=WindowStartupLocation.CenterScreen;}
        else FitScreen();
        scene=new Scene(engine){OpenSettings=ShowSettings,SaveNow=Save};Content=scene;
        SourceInitialized+=(_,_)=>new WindowInteropHelper(this).EnsureHandle();
        Loaded+=OnLoaded;
        timer.Tick+=Tick;
        SystemEvents.DisplaySettingsChanged+=OnDisplays;
        tray=new Forms.NotifyIcon{Icon=MakeIcon(),Text="陈皮 · 桌面小猫",Visible=true};
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("陈皮的小日子 · 设置",null,(_,_)=>Dispatcher.Invoke(ShowSettings));
        menu.Items.Add("召回猫猫",null,(_,_)=>Dispatcher.Invoke(()=>RunCommand(engine.Recall)));
        menu.Items.Add("显示 / 隐藏",null,(_,_)=>Dispatcher.Invoke(()=>hiddenByUser=!hiddenByUser));
        menu.Items.Add("退出陈皮",null,(_,_)=>Dispatcher.Invoke(Quit));tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowSettings);
        engine.RequestedAttention+=OnRequestedAttention;
        Closing+=(_,e)=>{if(!quitting){e.Cancel=true;hiddenByUser=true;}};
    }
    private void OnRequestedAttention(){if(!engine.State.Muted&&!fullScreen&&!hiddenByUser)PlayMeow(engine.State.Volume);}
    private static System.Drawing.Icon MakeIcon()
    {
        using var bmp=new System.Drawing.Bitmap(32,32);using(var g=System.Drawing.Graphics.FromImage(bmp))
        {g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(System.Drawing.Color.Transparent);using var b=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(130,146,167));g.FillEllipse(b,3,7,26,23);g.FillPolygon(b,new[]{new System.Drawing.Point(3,14),new System.Drawing.Point(4,1),new System.Drawing.Point(14,10)});g.FillPolygon(b,new[]{new System.Drawing.Point(18,10),new System.Drawing.Point(28,1),new System.Drawing.Point(29,15)});using var eye=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(238,203,137));g.FillEllipse(eye,8,15,5,6);g.FillEllipse(eye,20,15,5,6);}
        IntPtr h=bmp.GetHicon();var icon=(System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();DestroyIcon(h);return icon;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(initialized)return;
        initialized=true;
        if(!Preview)FitScreen();UpdateLayout();
        if(args.Contains("--floating"))engine.State.Floating=true;
        ApplyLayer();scene.LayoutWorld();CompositionTarget.Rendering+=RenderFrame;timer.Start();Save();
        string? snapshot=Program.Option(args,"--snapshot");
        if(snapshot is not null)
        {
            double seconds=double.TryParse(Program.Option(args,"--snapshot-delay"),out var requested)?Math.Clamp(requested,1,60):2;
            var shot=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};shot.Tick+=(_,_)=>{shot.Stop();if(quitting)return;scene.SavePreview(snapshot);File.WriteAllText(snapshot+".json",System.Text.Json.JsonSerializer.Serialize(new{attached,parent=Native.GetParent(new WindowInteropHelper(this).Handle).ToInt64(),windowStatus,boot,awake=Native.AwakeSeconds,engine.Action,scene.DisplayedClip,scene.DisplayedFrame,engine.State.X,engine.State.Y,engine.State.NestPosition,engine.State.FoodPosition,engine.State.WaterPosition,engine.State.LitterPosition,engine.State.RestDuration,engine.State.RestElapsed,engine.State.StillSeconds,engine.ToyHeld,engine.ToyOverlaps,scene.LiftTransitions,scene.MaxLiftTransitionMs,renderedFrames,averageFrameMs=renderedFrames>1?frameIntervals/(renderedFrames-1)*1000:0,maxFrameMs=maxFrameInterval*1000,averageDrawMs=scene.RenderMilliseconds/Math.Max(1,scene.RenderCount)}));if(args.Contains("--exit-after-snapshot"))Quit();};shot.Start();
        }
        if(args.Contains("--settings"))ShowSettings();
    }
    private void FitScreen()
    {
        var work=SystemParameters.WorkArea;
        Left=work.Left;Top=work.Top;Width=work.Width;Height=work.Height;
    }
    private void OnDisplays(object? sender,EventArgs e)=>Dispatcher.BeginInvoke(()=>{if(!quitting&&!Preview)ReplaceDisplayWindow();});
    private void ApplyLayer()
    {
        if(Preview){windowStatus="交互预览";return;}
        attached=Native.Attach(this,engine.State.Floating);
        windowStatus=engine.State.Floating?"悬浮模式 · 全屏自动避让":attached?"桌面模式 · 普通窗口可覆盖":"桌面挂接未成功，请切换悬浮模式";
        if(layerStatus is not null)layerStatus.Text=LayerDescription;
        scene.InvalidateVisual();
    }
    private void SwitchLayer(bool floating)
    {
        if(quitting||engine.State.Floating==floating)return;
        engine.State.Floating=floating;
        if(Preview){Save();return;}
        ReplaceDisplayWindow();
    }
    private void ReplaceDisplayWindow()
    {
        if(quitting)return;
        // WPF owns the layered HWND's render target. Recreate the host rather than
        // reparenting a live target between top-level and Explorer child windows.
        scene.CancelDrag();
        bool reopenSettings=settings is not null;
        var settingsBounds=settings is null?(Rect?)null:new Rect(settings.Left,settings.Top,settings.Width,settings.Height);
        double awake=Native.AwakeSeconds;
        engine.AdvanceNeeds(Math.Max(0,awake-previousAwake));engine.State.AwakeSeconds=awake;previousAwake=awake;
        var remainingArgs=args.Contains("--layer-diagnostics")?new[]{"--layer-diagnostics"}:Array.Empty<string>();
        var next=new PetWindow(store,remainingArgs,engine){hiddenByUser=hiddenByUser};
        StopHost();settings?.Close();Close();
        System.Windows.Application.Current.MainWindow=next;
        next.Show();
        if(reopenSettings)
        {
            next.ShowSettings();
            if(settingsBounds is Rect bounds&&next.settings is Window panel)
            {panel.Left=bounds.Left;panel.Top=bounds.Top;panel.Width=bounds.Width;panel.Height=bounds.Height;}
        }
    }
    private string LayerDescription=>windowStatus+"\n"+(engine.State.Floating?"小猫显示在普通窗口上；退出全屏后自动恢复。":"小猫留在桌面上，普通窗口会遮住它；重新打开悬浮可立即显示。");
    private void RenderFrame(object? sender,EventArgs e)
    {
        if(quitting)return;
        double now=animationClock.Elapsed.TotalSeconds,dt=now-previousAnimation;
        if(dt<1.0/120)return;
        previousAnimation=now;
        if(renderedFrames>0){frameIntervals+=dt;maxFrameInterval=Math.Max(maxFrameInterval,dt);}renderedFrames++;
        if(scene.Visibility==Visibility.Visible)scene.InputTick(Math.Min(.12,dt));
        else engine.ObservePointer(0,false,new Spot());
        engine.Update(Math.Min(.12,dt),DateTime.Now.Hour);
    }
    private void Tick(object? sender,EventArgs e)
    {
        if(quitting)return;
        double now=animationClock.Elapsed.TotalSeconds,awake=Native.AwakeSeconds;
        engine.AdvanceNeeds(Math.Max(0,awake-previousAwake));previousAwake=awake;engine.State.AwakeSeconds=awake;
        if(now-lastWindowCheck>.5)
        {
            lastWindowCheck=now;
            var foreground=Native.GetForegroundWindow();
            var fullscreenDecision=Preview?new FullscreenDecision(false,"preview"):Native.CheckFullScreen(foreground,new WindowInteropHelper(this).Handle);
            bool nextFull=fullscreenDecision.Hide;
            if((nextFull||hiddenByUser)&&scene.IsInteracting){scene.CancelDrag();}fullScreen=nextFull;
            scene.Visibility=fullScreen||hiddenByUser?Visibility.Hidden:Visibility.Visible;
            if(!Preview&&!engine.State.Floating && (Native.GetParent(new WindowInteropHelper(this).Handle)!=Native.DesktopHost()||!Native.IsWindow(Native.GetParent(new WindowInteropHelper(this).Handle))))
            {if(Native.DesktopHost()!=IntPtr.Zero){ReplaceDisplayWindow();return;}}
            if(args.Contains("--layer-diagnostics"))File.WriteAllText(Path.Combine(store.DirectoryPath,"window-diagnostics.json"),System.Text.Json.JsonSerializer.Serialize(new {native=Native.WindowDiagnostics(this),engine.State.Floating,fullScreen,fullscreenReason=fullscreenDecision.Reason,foreground=foreground.ToInt64(),hiddenByUser,sceneVisibility=scene.Visibility.ToString(),renderedFrames,engine.Action,engine.ActionTime,engine.State.Sleeping,engine.State.SleepingInNest,engine.State.CareRequest,engine.State.Guiding,engine.State.X,engine.State.Y}));
        }
        if(!scene.IsInteracting&&(now-lastSave>10 ||engine.Dirty&&now-lastSave>2)){Save();lastSave=now;engine.Dirty=false;}
    }
    internal static string ActionName(string action)=>action switch {"walk"=>"散步", "sleep"=>"睡觉", "eat"=>"吃饭", "drink"=>"喝水", "toilet"=>"上厕所", "bury"=>"埋猫砂", "drag"=>"被拎着", "pet"=>"撒娇", "wake"=>"伸懒腰", "cute"=>"卖萌","sit"=>"坐着休息","rub"=>"蹭鼠标","paw"=>"伸爪互动","roll"=>"翻肚皮",var x when x.StartsWith("toy-")=>"玩逗猫棒",var x when x.StartsWith("beg")=>"叫主人",_=>"发呆"};
    private void Save()
    {
        try{store.QueueSave(engine.State);}catch(Exception e){store.Log("save",e);}
    }
    private void RunCommand(Action command)
    {scene.CancelDrag();hiddenByUser=false;command();Save();}
    private void Quit()
    {
        scene.CancelDrag();StopHost();Save();store.Flush();settings?.Close();Close();System.Windows.Application.Current.Shutdown();
    }
    private void StopHost()
    {quitting=true;timer.Stop();CompositionTarget.Rendering-=RenderFrame;engine.RequestedAttention-=OnRequestedAttention;SystemEvents.DisplaySettingsChanged-=OnDisplays;tray.Visible=false;tray.Dispose();}
    private static Brush Color(string value)=>new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
    private static TextBlock Text(string value,double size=14,string color="#685C51")=>new(){Text=value,FontSize=size,Foreground=Color(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    private static Button Button(string text,Action click)
    {
        var b=new Button{Content=text,Padding=new Thickness(13,8,13,8),Margin=new Thickness(0,0,8,8),Background=Color("#F1EBDD"),Foreground=Color("#514C43"),BorderBrush=Color("#DDD2C0"),BorderThickness=new Thickness(1),Cursor=System.Windows.Input.Cursors.Hand};b.Click+=(_,_)=>click();return b;
    }
    private CheckBox Toggle(string label,bool initial,Action<bool> change)
    {
        var b=new CheckBox{Content=label,IsChecked=initial,FontSize=14,Margin=new Thickness(0,0,0,14),Foreground=Color("#514C43")};b.Checked+=(_,_)=>change(true);b.Unchecked+=(_,_)=>change(false);return b;
    }
    private void ShowSettings()
    {
        if(settings is not null){settings.Activate();return;}
        var w=new Window{Title="陈皮 · 小猫的家",Width=490,Height=760,MinWidth=430,MinHeight=580,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Color("#FBF8F1"),FontFamily=new FontFamily("Microsoft YaHei UI"),ResizeMode=ResizeMode.CanResize};settings=w;
        var body=new StackPanel{Margin=new Thickness(28)};
        body.Children.Add(Text("CHENPI  /  DESKTOP COMPANION",10,"#8B9986"));
        body.Children.Add(Text("陈皮的小日子",28,"#394D47"));
        body.Children.Add(Text("一只住在桌面上的小猫。\n不用打卡，也没有惩罚，记得偶尔摸摸它。",13));
        if(store.Warning is not null)body.Children.Add(Text(store.Warning,12));
        if(store.WriteError is not null)body.Children.Add(Text("存档写入失败："+store.WriteError,12));
        body.Children.Add(Toggle("悬浮在普通窗口上（全屏时隐藏）",engine.State.Floating,SwitchLayer));
        body.Children.Add(Toggle("开机显示小猫",AutoStartEnabled(),on=>{try{SetAutoStart(on);}catch(Exception e){System.Windows.MessageBox.Show(e.Message,"自启设置未保存");}}));
        body.Children.Add(Toggle("静音（保留动作提醒）",engine.State.Muted,on=>{engine.State.Muted=on;Save();}));
        body.Children.Add(Text("小猫与物件大小",12));
        var size=new Slider{Minimum=.7,Maximum=1.4,Value=engine.State.Scale,TickFrequency=.1,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,0,15)};
        size.ValueChanged+=(_,_)=>{engine.State.Scale=size.Value;scene.LayoutWorld();Save();};body.Children.Add(size);
        body.Children.Add(Text("叫声音量",12));
        var volume=new Slider{Minimum=0,Maximum=1,Value=engine.State.Volume,Margin=new Thickness(0,0,0,18)};volume.ValueChanged+=(_,_)=>{engine.State.Volume=volume.Value;Save();};body.Children.Add(volume);
        var controls=new WrapPanel();controls.Children.Add(Button("召回猫猫",()=>RunCommand(engine.Recall)));controls.Children.Add(Button("恢复摆放",()=>RunCommand(()=>{engine.Layout(scene.WorldWidth,scene.WorldHeight,true);engine.Recall();})));controls.Children.Add(Button("显示 / 隐藏",()=>hiddenByUser=!hiddenByUser));body.Children.Add(controls);
        body.Children.Add(Text("体验动作",14,"#394D47"));
        body.Children.Add(Text("按钮会优先执行对应动作。吃喝真实消耗库存，空盆请先补给，猫砂满时请先清理。",11));
        var demo=new WrapPanel();
        foreach(var (label,action) in new[]{("吃饭","eat"),("喝水","drink"),("猫砂盆","toilet"),("回窝","sleep"),("卖萌","cute")})demo.Children.Add(Button(label,()=>RunCommand(()=>engine.Demo(action))));
        body.Children.Add(demo);
        body.Children.Add(Text("点饭盆加粮 · 点水盆加水 · 点猫砂盆清理\n拖动物品可以搬家，位置会自动记住。\n点猫摸摸，长按或按住移动来拎起；拖进窝里松手睡觉。\n鼠标停在醒着的猫身上片刻，它会过来蹭蹭。\n点窝旁的羽毛棒拿起，靠近小猫逗它，再点一次归位。",12));
        layerStatus=Text(LayerDescription,11);body.Children.Add(layerStatus);
        body.Children.Add(Text("这是陈皮的体验版本，小猫外观还会继续完善。",11,"#9B7B5E"));
        body.Children.Add(Button("退出陈皮",Quit));
        w.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};w.Closed+=(_,_)=>{settings=null;layerStatus=null;};w.Show();w.Activate();
    }
    private static bool AutoStartEnabled(){using var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");return key?.GetValue("Chenpi") is string;}
    private static void SetAutoStart(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        if(enabled)
        {
            string host=Environment.ProcessPath!;
            string command=Path.GetFileNameWithoutExtension(host).Equals("dotnet",StringComparison.OrdinalIgnoreCase)?$"\"{host}\" \"{typeof(Program).Assembly.Location}\"":$"\"{host}\"";
            key.SetValue("Chenpi",command);
        }
        else key.DeleteValue("Chenpi",false);
    }
    private static void PlayMeow(double volume)
    {
        // A quiet synthesized placeholder chirp; replace with licensed cat audio later.
        System.Threading.Tasks.Task.Run(()=>
        {
            try
            {
                const int rate=22050;int samples=(int)(rate*.19);
                using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.ASCII,true))
                {writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);for(int i=0;i<samples;i++){double t=(double)i/rate;writer.Write((short)(Math.Sin(2*Math.PI*(650*t+500*t*t))*Math.Sin(Math.PI*i/samples)*3000*volume));}}
                stream.Position=0;using var player=new System.Media.SoundPlayer(stream);player.PlaySync();
            }catch(Exception e){Trace.WriteLine(e);}
        });
    }
}
