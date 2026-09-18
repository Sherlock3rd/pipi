using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Chenpi;

// Isolated reviewer of the installed manifest; never touches personal pet state.
internal sealed class VideoReviewWindow : Window
{
    private sealed record ReviewClip(string Id,string Label,string[] Files,double Fps);
    private readonly string root;
    private readonly bool preparedMatte;
    private readonly List<ReviewClip> clips=new();
    private readonly List<BitmapSource> frames=new();
    private readonly Image cat=new(){Width=360,Height=360,Stretch=Stretch.Uniform};
    private readonly TextBlock status=new(){FontSize=13,Margin=new Thickness(16,8,16,12)};
    private readonly Slider seek=new(){Minimum=0,Margin=new Thickness(16,8,16,8)};
    private readonly ComboBox choose=new(){MinWidth=310,Margin=new Thickness(8)};
    private readonly ComboBox sequence=new(){MinWidth=220,Margin=new Thickness(8)};
    private readonly Border stage=new(){Background=new SolidColorBrush(Color.FromRgb(231,232,223)),MinHeight=390};
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(15)};
    private readonly Dictionary<string,string[]> routes=new()
    {
        ["单条查看"]=Array.Empty<string>(),
        ["右行 → 转身 → 左行"]=new[]{"11","15","12","14"},
        ["左行 → 转身 → 右起步"]=new[]{"13","16","10"},
        ["正面 → 右起停 → 正面"]=new[]{"06","10","11","07"},
        ["正面 → 左行 → 正面"]=new[]{"08","12","14","13","09"},
        ["正面 → 入睡 → 醒来 → 正面"]=new[]{"17","19","20","21","18"}
        ,["吃饭进入 → 咀嚼 → 抬头"]=new[]{"22","23","24"}
        ,["喝水进入 → 舔水 → 抬头"]=new[]{"22","25","24"}
        ,["如厕 → 起身 → 刨砂"]=new[]{"26","27","28","29","30","31"}
        ,["提起 → 悬空 → 放下"]=new[]{"32","33","34"}
        ,["右抬爪 → 抓拨 → 收爪"]=new[]{"29","35","31"}
        ,["侧躺 → 轻滚 → 坐起"]=new[]{"40","41","42"}
    };
    private ReviewClip? current;
    private double elapsed,previous;
    private bool playing=true,sync,selectingRoute;
    private string[] activeRoute=Array.Empty<string>();
    private int routeIndex;
    private readonly string? snapshot;
    private readonly double snapshotAfter;
    private bool savedSnapshot;
    private readonly bool closeAfterSnapshot;

    public VideoReviewWindow(string directory,string[] args)
    {
        root=Path.GetFullPath(directory);
        using(var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"manifest.json"))))
            preparedMatte=settings.RootElement.TryGetProperty("videoMattePrepared",out var matte)&&matte.GetBoolean();
        closeAfterSnapshot=args.Contains("--exit-after-snapshot");
        snapshot=Program.Option(args,"--snapshot");
        snapshotAfter=double.TryParse(Program.Option(args,"--snapshot-delay"),out var seconds)?seconds:3;
        Title="陈皮 · 基础动作视频评审";Width=1060;Height=720;MinWidth=850;MinHeight=620;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brushes.WhiteSmoke;
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));
        var manifest=doc.RootElement;
        foreach(var item in manifest.GetProperty("reviewClips").EnumerateArray())
        {
            string id=item.GetProperty("id").GetString()!;
            var files=manifest.GetProperty("animations").GetProperty(id).EnumerateArray().Select(f=>f.GetString()!).ToArray();
            clips.Add(new(id,item.GetProperty("label").GetString()!,files,manifest.GetProperty("clips").GetProperty(id).GetProperty("fps").GetDouble()));
        }
        var panel=new DockPanel();Content=panel;
        var heading=new StackPanel{Margin=new Thickness(16,12,16,0)};
        heading.Children.Add(new TextBlock{Text="陈皮 · 动作评审",FontSize=24,FontWeight=FontWeights.SemiBold});
        heading.Children.Add(new TextBlock{Text="查看全部动作与连续衔接。按现有视频接入运行，原始视频均已保留。",FontSize=13,Margin=new Thickness(0,6,0,4)});
        DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
        var bar=new WrapPanel{Margin=new Thickness(8)};
        foreach(var clip in clips)choose.Items.Add(clip.Label);
        choose.SelectionChanged+=(_,_)=>
        {
            if(choose.SelectedIndex<0)return;
            if(!selectingRoute){activeRoute=Array.Empty<string>();sequence.SelectedIndex=0;}
            LoadClip(clips[choose.SelectedIndex]);
        };
        bar.Children.Add(choose);
        foreach(var name in routes.Keys)sequence.Items.Add(name);
        sequence.SelectionChanged+=(_,_)=>
        {
            if(sequence.SelectedItem is not string name)return;
            activeRoute=routes[name];routeIndex=0;
            if(activeRoute.Length>0)SelectRouteClip();
        };
        sequence.SelectedIndex=0;bar.Children.Add(sequence);
        Button Button(string label,Action action){var b=new Button{Content=label,Padding=new Thickness(10,7,10,7),Margin=new Thickness(4)};b.Click+=(_,_)=>action();return b;}
        var play=Button("暂停 / 播放",()=>playing=!playing);bar.Children.Add(play);
        bar.Children.Add(Button("上一帧",()=>Step(-1)));bar.Children.Add(Button("下一帧",()=>Step(1)));
        bool dark=true;stage.Background=new SolidColorBrush(Color.FromRgb(24,27,34));
        bar.Children.Add(Button("深浅背景",()=>{dark=!dark;stage.Background=dark?new SolidColorBrush(Color.FromRgb(24,27,34)):new SolidColorBrush(Color.FromRgb(231,232,223));}));
        bool large=true;bar.Children.Add(Button("实际大小 / 放大",()=>{large=!large;cat.Width=cat.Height=large?360:288;}));
        bar.Children.Add(Button("查看原视频",()=>
        {
            if(current is null)return;
            string sourceRoot=Program.Option(args,"--video-sources")??Path.Combine(root,"..");
            if(int.TryParse(current.Id[6..],out int number)&&number>=22)
                sourceRoot=Path.GetFullPath(Path.Combine(sourceRoot,"..","..","care-v3","returned"));
            string source=Path.GetFullPath(Path.Combine(sourceRoot,current.Id=="video-right"?"00":current.Id[6..],"source.mp4"));
            if(File.Exists(source))Process.Start(new ProcessStartInfo(source){UseShellExecute=true});
        }));
        DockPanel.SetDock(bar,Dock.Top);panel.Children.Add(bar);
        var bottom=new StackPanel();bottom.Children.Add(seek);bottom.Children.Add(status);
        DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);
        stage.Child=cat;panel.Children.Add(stage);
        RenderOptions.SetBitmapScalingMode(cat,BitmapScalingMode.HighQuality);
        seek.ValueChanged+=(_,_)=>{if(!sync&&current is not null){elapsed=seek.Value/current.Fps;playing=false;RenderFrame();}};
        choose.SelectedIndex=Math.Max(0,clips.FindIndex(c=>c.Id==Program.Option(args,"--clip")));
        if(int.TryParse(Program.Option(args,"--frame"),out int requestedFrame)&&current is not null)
        {elapsed=Math.Clamp(requestedFrame,0,frames.Count-1)/current.Fps;playing=false;RenderFrame();}
        timer.Tick+=(_,_)=>Tick();timer.Start();Closed+=(_,_)=>timer.Stop();
    }
    private void LoadClip(ReviewClip clip)
    {
        current=clip;frames.Clear();
        foreach(var relative in clip.Files)
        {
            string path=Path.GetFullPath(Path.Combine(root,relative));
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Frame outside review directory");
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(path);image.EndInit();image.Freeze();frames.Add(Scene.PrepareBitmap(image,false,out _,8,!preparedMatte));
        }
        elapsed=0;previous=clock.Elapsed.TotalSeconds;sync=true;seek.Maximum=Math.Max(0,frames.Count-1);seek.Value=0;sync=false;RenderFrame();
    }
    private void SelectRouteClip()
    {
        selectingRoute=true;
        int index=clips.FindIndex(c=>c.Id=="video-"+activeRoute[routeIndex]);
        if(choose.SelectedIndex==index)LoadClip(clips[index]);else choose.SelectedIndex=index;
        selectingRoute=false;playing=true;
    }
    private void Step(int direction)
    {
        if(current is null)return;playing=false;
        int index=Math.Clamp((int)Math.Floor(elapsed*current.Fps+1e-6)+direction,0,frames.Count-1);
        elapsed=index/current.Fps;RenderFrame();
    }
    private void Tick()
    {
        double now=clock.Elapsed.TotalSeconds,delta=now-previous;previous=now;
        if(playing&&current is not null)
        {
            elapsed+=delta;double duration=frames.Count/current.Fps;
            if(elapsed>=duration)
            {
                if(activeRoute.Length>0){routeIndex=(routeIndex+1)%activeRoute.Length;SelectRouteClip();}
                else elapsed%=duration;
            }
        }
        RenderFrame();
        if(snapshot is not null&&!savedSnapshot&&clock.Elapsed.TotalSeconds>=snapshotAfter)
        {
            savedSnapshot=true;UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(snapshot))!);
            using(var stream=File.Create(snapshot))encoder.Save(stream);
            File.WriteAllText(snapshot+".json",JsonSerializer.Serialize(new{clip=current?.Id,frame=(int)(elapsed*(current?.Fps??24)),count=frames.Count,fps=current?.Fps,playing,route=activeRoute}));
            if(closeAfterSnapshot)Close();
        }
    }
    private void RenderFrame()
    {
        if(current is null||frames.Count==0)return;
        int index=Math.Clamp((int)Math.Floor(elapsed*current.Fps),0,frames.Count-1);cat.Source=frames[index];
        sync=true;seek.Value=index;sync=false;
        status.Text=$"{current.Label}    第 {index+1} / {frames.Count} 帧    {index/current.Fps:0.00} 秒    {current.Fps:0.##} 帧/秒    {(playing?"播放中":"已暂停，可逐帧查看")}";
    }
}
