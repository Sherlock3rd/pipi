using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Collections;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using System.Globalization;

namespace Chenpi;
internal sealed class BehaviorEditorWindow : Window
{
    private static readonly BehaviorGraph.Node[] Nodes=BehaviorGraph.Nodes;
    private readonly PetEngine engine;
    private readonly BehaviorSettingsStore store;
    private readonly Action save;
    private readonly Action<Action> command;
    private BehaviorSettings draft;
    private string view="global",selected="root";
    private readonly Canvas canvas=new();
    private readonly StackPanel inspector=new(){Margin=new Thickness(16)};
    private readonly Dictionary<string,Border> nodeBorders=new();
    private readonly Dictionary<string,TextBox> fields=new();
    private readonly Dictionary<string,string> invalid=new();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly CheckBox restart=new(){Content="重新计时三项照料期限"};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    public BehaviorEditorWindow(PetEngine engine,BehaviorSettingsStore store,Action save,Action<Action> command)
    {
        this.engine=engine;this.store=store;this.save=save;this.command=command;draft=engine.Settings.Copy();
        Title="陈皮 · 行为工作台";Width=1320;Height=900;MinWidth=1000;MinHeight=700;
        var root=new DockPanel();Content=root;var top=new StackPanel{Margin=new Thickness(15)};DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(Label("行为工作台 · 流程图与参数",24));var buttons=new WrapPanel();top.Children.Add(buttons);
        foreach(var (label,id) in new[]{("全局流程","global"),("睡姿关系","poses"),("启动开场","startup")})buttons.Children.Add(Button(label,()=>{view=id;BuildGraph();}));
        buttons.Children.Add(Button("全部参数",()=>{selected="";BuildInspector();}));buttons.Children.Add(Button("保存并应用",Apply));
        buttons.Children.Add(Button("撤销草稿",()=>{draft=engine.Settings.Copy();invalid.Clear();BuildInspector();}));buttons.Children.Add(Button("恢复默认草稿",()=>{draft=new();invalid.Clear();BuildInspector();}));
        buttons.Children.Add(Button("导入 JSON",()=>_ = Import()));buttons.Children.Add(Button("导出 JSON",()=>_ = Export()));top.Children.Add(restart);top.Children.Add(status);
        var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("*,350")};root.Children.Add(grid);
        grid.Children.Add(new ScrollViewer{Content=canvas,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto});
        var side=new ScrollViewer{Content=inspector};Grid.SetColumn(side,1);grid.Children.Add(side);
        BuildGraph();BuildInspector();timer.Tick+=(_,_)=>PaintNodes();timer.Start();Closed+=(_,_)=>timer.Stop();
    }
    private static SolidColorBrush Brush(string hex)=>new(Color.Parse(hex));
    private static TextBlock Label(string text,double size=14,string color="#527166")=>new(){Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap};
    private static Button Button(string title,Action action){var b=new Button{Content=title,Margin=new Thickness(0,0,8,8)};b.Click+=(_,_)=>action();return b;}
    private void Line(double x1,double y1,double x2,double y2)=>Connect(new[]{new Point(x1,y1),new Point(x1,y2),new Point(x2,y2)});
    private void Connect(Point[] points,bool both=false)
    {
        points=points.Where((p,i)=>i==0||p!=points[i-1]).ToArray();if(points.Length<2)return;
        canvas.Children.Add(new Polyline{Stroke=Brush("#A4BAB1"),StrokeThickness=2,Points=new AvaloniaList<Point>(points)});
        void Arrow(Point tip,Point before){var direction=(Vector)(tip-before);direction/=direction.Length;var side=new Vector(-direction.Y,direction.X);canvas.Children.Add(new Polygon{Fill=Brush("#A4BAB1"),Points=new AvaloniaList<Point>{tip,tip-direction*8+side*4,tip-direction*8-side*4}});}
        Arrow(points[^1],points[^2]);if(both)Arrow(points[0],points[1]);
    }
    private void BranchRow(double top,params (string Id,double X)[] items)
    {foreach(var item in items)Connect(new[]{new Point(390,top+40),new Point(410,top+40),new Point(410,top-18),new Point(item.X+88,top-18),new Point(item.X+88,top)});foreach(var item in items)Card(item.Id,item.X,top,176);}
    private void Card(string id,double x,double y,double width=270,string? title=null)
    {
        var node=Nodes.First(n=>n.Id==id);var body=new StackPanel{Margin=new Thickness(15,12)};body.Children.Add(Label(title??node.Title,18));
        var hint=Label("单击查看参数 · 双击执行",11);body.Children.Add(hint);
        var border=new Border{Width=width,MinHeight=80,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1.5),Child=body};
        ToolTip.SetTip(border,node.Detail);border.PointerPressed+=(_,e)=>{selected=id;BuildInspector();PaintNodes();if(e.ClickCount==2)command(()=>status.Text=engine.ExecuteBehavior(id));e.Handled=true;};Canvas.SetLeft(border,x);Canvas.SetTop(border,y);canvas.Children.Add(border);nodeBorders[id]=border;
    }
    private void PaintNodes()
    {foreach(var (id,border) in nodeBorders){bool active=id==engine.ActiveBehaviorNode||engine.ActiveBehaviorNode=="poses"&&id=="pose-"+engine.RelaxedPose;border.Background=Brush(active?"#E1F2E9":"#FFFFFF");border.BorderBrush=Brush(id==selected?"#247A68":active?"#78AD94":"#D6E1D9");}}
    private void BuildInspector()
    {
        inspector.Children.Clear();fields.Clear();var node=Nodes.FirstOrDefault(n=>n.Id==selected);
        inspector.Children.Add(Label(node?.Title??"全部可调参数",22));inspector.Children.Add(Label(node?.Detail??"修改先进入草稿，保存后应用。",12));
        if(node is not null)inspector.Children.Add(Button("执行此动作",()=>command(()=>status.Text=engine.ExecuteBehavior(node.Id))));
        if(selected=="wall")
        {inspector.Children.Add(Label(engine.WallStatus,12));foreach(string side in new[]{"left","right"})inspector.Children.Add(Button(side=="left"?"左侧扶墙":"右侧扶墙",()=>command(()=>status.Text=engine.ExecuteBehavior("wall-"+side))));}
        foreach(var p in BehaviorSettings.Catalog.Where(p=>node is null||node.Groups.Contains(p.Group)))
        {
            inspector.Children.Add(Label(p.Label+"（"+p.Unit+"）",14));var box=new TextBox{Text=invalid.GetValueOrDefault(p.Key)??draft.Get(p.Key).ToString(CultureInfo.InvariantCulture),Margin=new Thickness(0,4,0,4)};
            box.TextChanged+=(_,_)=>{if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value>=p.Min&&value<=p.Max){draft.Values[p.Key]=value;invalid.Remove(p.Key);}else invalid[p.Key]=box.Text??"";};
            fields[p.Key]=box;inspector.Children.Add(box);inspector.Children.Add(Label(p.Help+" 范围 "+p.Min+"～"+p.Max,11));
        }
    }
    private void Apply()
    {try{if(invalid.Count>0)throw new InvalidDataException("请修正无效参数："+string.Join("、",invalid.Keys));draft.Validate();store.Save(draft);engine.ApplyBehaviorSettings(draft,restart.IsChecked==true);restart.IsChecked=false;save();status.Text="已保存并应用；当前动作完整衔接，后续决策使用新参数。";}catch(Exception e){status.Text=e.Message;}}
    private async Task Import()
    {try{var picks=await StorageProvider.OpenFilePickerAsync(new(){Title="导入行为参数",AllowMultiple=false});if(picks.Count==0)return;await using var stream=await picks[0].OpenReadAsync();using var reader=new StreamReader(stream);draft=BehaviorSettingsStore.Parse(await reader.ReadToEndAsync());invalid.Clear();BuildInspector();status.Text="已导入草稿，保存后应用。";}catch(Exception e){status.Text=e.Message;}}
    private async Task Export()
    {try{if(invalid.Count>0)throw new InvalidDataException("请先修正无效参数");draft.Validate();var file=await StorageProvider.SaveFilePickerAsync(new(){Title="导出行为参数",SuggestedFileName="chenpi-behavior.json"});if(file is null)return;await using var stream=await file.OpenWriteAsync();stream.SetLength(0);await System.Text.Json.JsonSerializer.SerializeAsync(stream,draft,new System.Text.Json.JsonSerializerOptions{WriteIndented=true});status.Text="已导出当前草稿。";}catch(Exception e){status.Text=e.Message;}}
    private void BuildGraph()
    {
        canvas.Children.Clear();nodeBorders.Clear();canvas.Width=1040;canvas.Height=view=="global"?1220:view=="startup"?1120:950;
        if(view=="global")
        {
            var caption=Label("优先级骨架  /  上层命中后先执行；点击节点可查看实际条件",17,"#527166");Canvas.SetLeft(caption,30);Canvas.SetTop(caption,18);canvas.Children.Add(caption);
            string[] rows={"input","safety","toy","request","care","movement","rest"};
            double[] rowY={110,247,384,521,658,795,950};
            for(int i=0;i<rows.Length;i++)Line(45,80,100,rowY[i]+40);
            for(int i=0;i<rows.Length;i++)Card(rows[i],100,rowY[i],290);
            Line(390,150,440,150);Card("click",440,110,550);
            Line(390,561,440,561);Card("guide",440,521,550);
            BranchRow(658,("food",440),("water",627),("litter",814));
            BranchRow(795,("destinations",440),("wall",730));
            BranchRow(950,("poses",440),("gesture",730));
            Connect(new[]{new Point(390,990),new Point(410,990),new Point(410,1069),new Point(575,1069),new Point(575,1087)});
            Connect(new[]{new Point(410,1069),new Point(860,1069),new Point(860,1087)});
            Card("night",440,1087,270);Card("nest",730,1087,260);
            var hint=Label("物品避让、真实姿态过渡、实际摄入扣库存是固定执行约束。照料不是随机抽奖。",13,"#6F8078");Canvas.SetLeft(hint,440);Canvas.SetTop(hint,320);hint.Width=530;canvas.Children.Add(hint);
        }
        else if(view=="startup")
        {
            Card("startup",45,25,950);
            string[] steps={"intro-sleep","intro-wake","intro-run","intro-pace","intro-rub","intro-call","intro-sit","intro-done"};
            for(int i=0;i<steps.Length;i++){double y=155+i*115;Connect(new[]{new Point(520,i==0?105:y-35),new Point(520,y)});Card(steps[i],145,y,750);}
        }
        else
        {
            Card("poses",45,25,950,"地面入睡 · 先按权重选姿态，再沿已有素材进入");
            Line(100,105,100,185);Card("rest",45,145,290,"何时进入休息");Card("click",365,145,300);Card("changes",695,145,300);
            Connect(new[]{new Point(180,320),new Point(210,320)},true);Connect(new[]{new Point(370,320),new Point(420,320)},true);
            Connect(new[]{new Point(600,320),new Point(790,320)},true);Connect(new[]{new Point(510,360),new Point(510,405)},true);
            Connect(new[]{new Point(600,340),new Point(660,340),new Point(660,445),new Point(790,445)},true);
            Card("seated",40,280,140);Card("pose-D",210,280,160);Card("pose-A",420,280,180);Card("pose-B",790,280,180);Card("pose-C",210,405,160);Card("pose-X",420,405,180);Card("pose-M",790,405,180);
            var note=Label("侧躺 / 露肚 / 舒展第三击 → 朝右站起；\n趴卧、侧坐均可直接朝左右站起。",14,"#977045");Canvas.SetLeft(note,45);Canvas.SetTop(note,505);note.Width=900;canvas.Children.Add(note);
            Card("gesture",45,550,950,"呼吸停留 → 等待间隔 → 短动作或换姿态 → 回到呼吸");
            foreach(var (id,x) in new[]{("gesture-D",45d),("gesture-A",290d),("gesture-B",535d),("gesture-X",780d)}){Line(x+35,630,x+35,660);Card(id,x,660,220);}
            Card("nest",45,820,950);
        }
        PaintNodes();
    }

}
