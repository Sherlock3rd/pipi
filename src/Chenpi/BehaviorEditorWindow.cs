using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Chenpi;

internal sealed class BehaviorEditorWindow : Window
{
    private sealed record Node(string Id,string Title,string Detail,string[] Groups);
    private static readonly Node[] Nodes={
        new("root","每帧决策 · 按优先级检查","上层条件优先；鼠标点击、拖拽和手动命令可以中断。下方是运行时决策总览，连线表示分支关系，不代表随机选择所有节点。",Array.Empty<string>()),
        new("input","① 输入与主动互动","长按／移动提起；普通点击互动；躺姿点击分级；手动吃喝、回窝优先执行。操作响应不设置随机失败概率。",new[]{"input"}),
        new("safety","② 物品与空位避让","休息时若占用家具区域，寻找最近可用地面空位。物件搬动后重算路径。脚底、碰撞和动作衔接沿用已校准规则。",Array.Empty<string>()),
        new("toy","③ 逗猫棒","羽毛进入外圈：连续追跑。进入内圈：停下抓拨。移开则恢复追跑；所有判定圈保持不可见。",new[]{"toy"}),
        new("request","④ 已有请求／带路","吃喝需同时满足到期、空盆及缺货等待；满砂等待后请求清理。请求在对应物品处发生，鼠标靠近后带路。",new[]{"request"}),
        new("guide","跟随 → 回头 → 物品示意","鼠标跟上才继续走，在安全空位回头等待，到对应食水盆或猫砂盆旁示意。补给后致谢；不会自动补库存。",new[]{"guide"}),
        new("care","⑤ 新照料需求","先检查有资格的缺货请求，再选择最早到期且有库存的照料。手动动作和入睡保护期间暂缓。每项独立计时，不按概率漏掉需求。",Array.Empty<string>()),
        new("food","吃饭","到期且有粮：到盆口 → 低头 → 实际吃饭 → 抬头 → 站姿直接离开。仅实际摄入扣粮。",new[]{"food"}),
        new("water","喝水","到期且有水：到杯口 → 低头 → 实际喝水 → 抬头 → 站姿直接离开。仅实际摄入扣水。",new[]{"water"}),
        new("litter","如厕／埋砂","到期且猫砂可用：进入盆面 → 如厕 → 换位转身 → 埋砂 → 离开。埋砂不等于清理。",new[]{"litter"}),
        new("rest","⑥ 自主休息","安静达到阈值或休息周期到期进入睡姿；到期继续睡，不强制漫游。夜间／低能量规则也可触发休息。",new[]{"rest"}),
        new("poses","选择入睡姿态","按同组权重抽取地面睡姿，经真实素材衔接进入；权重0只排除随机选入，不删除必要的过渡姿态。",new[]{"poses"}),
        new("seated","坐姿","趴卧从坐姿进入；当前站立直接趴下、趴卧直接站起素材尚缺。",Array.Empty<string>()),
        new("pose-D","趴卧","经坐姿进入；醒来先坐起。可调整随机入睡权重和趴卧小动作。",new[]{"gesture-D"}),
        new("pose-A","侧躺","连接趴卧、露肚、舒展和掩面；第三击使用直接起身片段。",new[]{"gesture-A","click"}),
        new("pose-B","露肚","由侧躺翻入；第三击直接朝右站起。",new[]{"gesture-B","click"}),
        new("pose-X","舒展","由侧躺翻入；第三击直接朝右站起。",new[]{"gesture-X","click"}),
        new("pose-M","掩面","由侧躺卷身进入；持续呼吸，不随机哈欠／叫声；单击放爪回侧躺。",Array.Empty<string>()),
        new("changes","侧躺之后换哪种姿态","趴卧会转侧躺；露肚／舒展会回侧躺；侧躺按权重选择露肚、舒展或掩面。掩面会持续睡眠直到互动或照料。",new[]{"changes"}),
        new("gesture","睡姿小动作与换姿","等待随机间隔后，按概率选择短动作或换睡姿。短动作在当前姿态内按权重选择哈欠、叫声、伸展；完整播放后回呼吸。",new[]{"gesture"}),
        new("gesture-D","趴卧 · 小动作","趴卧时的哈欠、叫声、伸展概率。三个权重只在趴卧短动作分支内比较。",new[]{"gesture-D"}),
        new("gesture-A","侧躺 · 小动作","侧躺时的哈欠、叫声、伸展概率。",new[]{"gesture-A"}),
        new("gesture-B","露肚 · 小动作","露肚时的哈欠、叫声、伸展概率。",new[]{"gesture-B"}),
        new("gesture-X","舒展 · 小动作","舒展时的哈欠、叫声、伸展概率。",new[]{"gesture-X"}),
        new("click","躺姿点击分级","侧躺／露肚／舒展：第一击轻抖、第二击强反应、第三击直接站起。窗口从第一击起算。掩面单击直接放爪。",new[]{"click"}),
        new("wall","屏边扶墙","仅在已靠近真实屏幕边缘且没有家具时触发；左右独立动画，完整落地后休息，不跨屏寻找墙面。",new[]{"wall"}),
        new("night","作息辅助条件","夜间且能量较低时倾向休息；不强制打断互动。起止小时相同表示关闭夜间加成，安静入睡仍有效。",new[]{"night"}),
        new("nest","猫窝／原蜷睡","拖进窝或手动回窝使用原蜷睡，保留坐垫支撑和窝沿遮挡。旧侧坐直接站立素材尚缺，不能通过改概率消除中间姿态。",new[]{"rest"}),
        new("movement","⑦ 移动与待机收尾","起身 → 真实转向 → 起步 → 移动 → 停步。吃喝收尾先判断避让，直接从站姿离开。固定动画时间不在此批量变速。",new[]{"movement"})
    };
    private readonly PetEngine engine;
    private readonly BehaviorSettingsStore store;
    private readonly Action saveState;
    private BehaviorSettings draft;
    private readonly Dictionary<string,string> invalid=new();
    private readonly Dictionary<string,TextBox> fields=new();
    private readonly Dictionary<string,TextBlock> weightLabels=new();
    private readonly Dictionary<string,Border> nodeBorders=new();
    private readonly Canvas canvas=new(){Width=1040,Height=1120};
    private readonly StackPanel inspector=new(){Margin=new Thickness(22)};
    private readonly ScrollViewer graphScroll=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock status=Label("点选流程节点，查看条件和参数。",12);
    private readonly TextBlock live=Label("",13);
    private readonly TextBlock history=Label("",11);
    private readonly CheckBox restartCare=new(){Content="将吃饭、喝水、如厕从现在重新计时",Margin=new Thickness(0,7,0,8),FontSize=12};
    private readonly Slider zoom=new(){Minimum=.45,Maximum=1.3,Value=.7,Width=105,Margin=new Thickness(8,0,12,0)};
    private readonly DispatcherTimer ticker=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private readonly Queue<string> transitions=new();
    private string selected="rest",view="global",prior="";
    private bool dirty,discardOnClose;
    internal bool AppliedInFixture {get;private set;}
    private static SolidColorBrush Brush(string color)=>new((Color)ColorConverter.ConvertFromString(color));
    private static TextBlock Label(string text,double size=14,string color="#304B47")=>new(){Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap};
    private static Button Button(string text,Action action,bool primary=false)
    {
        var b=new Button{Content=text,Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,8,0),Background=Brush(primary?"#246A60":"#F2F5F0"),Foreground=Brush(primary?"#FFFFFF":"#304B47"),BorderBrush=Brush("#C9D8D0"),Cursor=System.Windows.Input.Cursors.Hand};
        b.Click+=(_,_)=>action();return b;
    }
    public BehaviorEditorWindow(PetEngine engine,BehaviorSettingsStore store,Action saveState)
    {
        this.engine=engine;this.store=store;this.saveState=saveState;draft=engine.Settings.Copy();
        Title="陈皮 · 行为工作台";Width=1320;Height=900;MinWidth=980;MinHeight=680;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        FontFamily=new FontFamily("Microsoft YaHei UI");Background=Brush("#F5F6F1");
        var shell=new DockPanel{Background=Brush("#F5F6F1")};Content=shell;
        var header=new StackPanel{Margin=new Thickness(25,20,25,15)};DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
        header.Children.Add(Label("CHENPI  /  BEHAVIOR STUDIO",10,"#648A7F"));header.Children.Add(Label("陈皮的行为工作台",27));
        header.Children.Add(Label("看清决策顺序，调整节奏与概率。连线表示决策关系；编辑为草稿，保存后才影响猫猫。",12,"#697B75"));
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,0)};header.Children.Add(bar);
        bar.Children.Add(Button("全局流程",()=>{view="global";BuildGraph();Fit();}));bar.Children.Add(Button("睡姿与反应",()=>{view="sleep";BuildGraph();Fit();}));
        bar.Children.Add(Button("全部参数",()=>{selected="all";BuildInspector();}));bar.Children.Add(Button("适合窗口",Fit));
        bar.Children.Add(zoom);bar.Children.Add(Label("缩放",12));zoom.ValueChanged+=(_,_)=>canvas.LayoutTransform=new ScaleTransform(zoom.Value,zoom.Value);
        var bottom=new Border{Background=Brush("#FFFFFF"),BorderBrush=Brush("#DFE6DF"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(24,12,24,14)};DockPanel.SetDock(bottom,Dock.Bottom);shell.Children.Add(bottom);
        var footer=new DockPanel();bottom.Child=footer;var buttons=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);
        buttons.Children.Add(Button("导入",Import));buttons.Children.Add(Button("导出",Export));buttons.Children.Add(Button("撤销草稿",()=>ResetDraft(false)));buttons.Children.Add(Button("默认草稿",()=>ResetDraft(true)));buttons.Children.Add(Button("保存并应用",()=>Apply(),true));footer.Children.Add(status);
        var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(360)});shell.Children.Add(layout);
        graphScroll.Content=canvas;layout.Children.Add(graphScroll);
        var side=new DockPanel{Background=Brush("#FFFFFF")};Grid.SetColumn(side,1);layout.Children.Add(side);
        var livePanel=new StackPanel{Margin=new Thickness(22,16,22,12)};DockPanel.SetDock(livePanel,Dock.Bottom);side.Children.Add(livePanel);livePanel.Children.Add(live);livePanel.Children.Add(history);
        side.Children.Add(new ScrollViewer{Content=inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        BuildGraph();BuildInspector();Loaded+=(_,_)=>{Fit();RefreshLive();ticker.Start();};ticker.Tick+=(_,_)=>RefreshLive();
        if(store.Warning is string warning)status.Text=warning;
        Closing+=(_,e)=>{if(!dirty||discardOnClose)return;var answer=MessageBox.Show(this,"保存当前行为参数草稿？","尚有未保存的修改",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);if(answer==MessageBoxResult.Cancel||answer==MessageBoxResult.Yes&&!Apply())e.Cancel=true;};
        Closed+=(_,_)=>ticker.Stop();
    }
    private void Fit(){zoom.Value=Math.Clamp(Math.Min((graphScroll.ActualWidth-25)/canvas.Width,(graphScroll.ActualHeight-25)/canvas.Height),zoom.Minimum,1);canvas.LayoutTransform=new ScaleTransform(zoom.Value,zoom.Value);}
    private string Summary(string id)=>id switch {
        "rest"=>$"安静 {engine.Settings.Get("sleep.delay"):0.#} 秒入睡",
        "food" or "water" or "litter"=>$"独立周期 {engine.Settings.Get(id+".min"):0.#}～{engine.Settings.Get(id+".max"):0.#} 分钟",
        "request"=>$"需求成立 + 缺货等待 {engine.Settings.Get("request.delay"):0.#} 秒",
        "gesture"=>$"间隔 {engine.Settings.Get("relax.min"):0.#}～{engine.Settings.Get("relax.max"):0.#} 秒 · 短动作 {engine.Settings.Get("relax.gesture"):0.#}%",
        "poses"=>"趴卧 / 侧躺 / 露肚 / 舒展 / 掩面",
        "click"=>$"固定 {engine.Settings.Get("click.window"):0.#} 秒窗口 · 三档响应",
        "wall"=>engine.Settings.Get("wall.enabled")==0?"已关闭自主扶墙":$"屏边触发 · 循环 {engine.Settings.Get("wall.duration"):0.#} 秒",
        "toy"=>$"追逐 {engine.Settings.Get("toy.outer"):0} / 抓拨 {engine.Settings.Get("toy.inner"):0}",
        "night"=>$"{engine.Settings.Get("night.start"):00}:00—{engine.Settings.Get("night.end"):00}:00",
        "input"=>"点击 / 长按 / 悬停 / 手动命令", "safety"=>"先寻找可站／可躺的空位", "care"=>"有库存则行动；空盆到期才请求", "guide"=>"跟上就走，离开就回头等", "nest"=>"原蜷睡 · 坐垫支撑 / 窝沿遮挡", "movement"=>"起身 → 转向 → 起步 → 停步", "changes"=>"按当前姿态选择已有过渡", "seated"=>"先坐稳，再趴卧", var pose when pose.StartsWith("pose-")=>$"入睡占比 {engine.Settings.Get("pose."+pose[5..])/BehaviorSettings.Catalog.Where(p=>p.Group=="poses").Sum(p=>engine.Settings.Get(p.Key)):P1}", _=>"哈欠 / 叫声 / 伸展 · 独立权重"};
    private void Line(double x1,double y1,double x2,double y2)
    {
        Connect(new[]{new Point(x1,y1),new Point(x1,y2),new Point(x2,y2)});
    }
    private void Connect(Point[] points,bool both=false)
    {
        points=points.Where((p,i)=>i==0||p!=points[i-1]).ToArray();if(points.Length<2)return;
        canvas.Children.Add(new Polyline{Stroke=Brush("#A4BAB1"),StrokeThickness=2,Points=new PointCollection(points)});
        void Arrow(Point tip,Point before){var direction=tip-before;direction.Normalize();var side=new Vector(-direction.Y,direction.X);canvas.Children.Add(new Polygon{Fill=Brush("#A4BAB1"),Points=new PointCollection{tip,tip-direction*8+side*4,tip-direction*8-side*4}});}
        Arrow(points[^1],points[^2]);if(both)Arrow(points[0],points[1]);
    }
    private void BranchRow(double top,params (string Id,double X)[] items)
    {
        double bus=top-18,center=top+40;
        foreach(var item in items)Connect(new[]{new Point(390,center),new Point(410,center),new Point(410,bus),new Point(item.X+88,bus),new Point(item.X+88,top)});
        foreach(var item in items)Card(item.Id,item.X,top,176);
    }
    private void Card(string id,double x,double y,double width=270,string? title=null)
    {
        var node=Nodes.First(n=>n.Id==id);var panel=new StackPanel{Margin=new Thickness(15,12,15,10)};
        panel.Children.Add(Label(title??node.Title,18));panel.Children.Add(Label(Summary(id),12,"#677E75"));
        var border=new Border{Width=width,MinHeight=80,Background=Brush("#FFFFFF"),CornerRadius=new CornerRadius(12),BorderBrush=Brush("#D6E1D9"),BorderThickness=new Thickness(1.5),Child=panel,Cursor=System.Windows.Input.Cursors.Hand};
        border.MouseLeftButtonDown+=(_,_)=>{selected=id;BuildInspector();PaintNodes();};Canvas.SetLeft(border,x);Canvas.SetTop(border,y);canvas.Children.Add(border);nodeBorders[id]=border;
    }
    private void BuildGraph()
    {
        canvas.Children.Clear();nodeBorders.Clear();canvas.Width=1040;canvas.Height=view=="global"?1220:950;
        if(view=="global")
        {
            var caption=Label("优先级骨架  /  上层命中后先执行；点击节点可查看实际条件",17,"#527166");Canvas.SetLeft(caption,30);Canvas.SetTop(caption,18);canvas.Children.Add(caption);
            string[] rows={"input","safety","toy","request","care","rest","movement"};
            double[] rowY={110,247,384,521,658,795,1105};
            for(int i=0;i<rows.Length;i++)Line(45,80,100,rowY[i]+40);
            for(int i=0;i<rows.Length;i++)Card(rows[i],100,rowY[i],290);
            Line(390,150,440,150);Card("click",440,110,550);
            Line(390,561,440,561);Card("guide",440,521,550);
            BranchRow(658,("food",440),("water",627),("litter",814));
            BranchRow(795,("poses",440),("gesture",627),("wall",814));
            Connect(new[]{new Point(390,835),new Point(410,835),new Point(410,914),new Point(575,914),new Point(575,932)});
            Connect(new[]{new Point(410,914),new Point(860,914),new Point(860,932)});
            Card("night",440,932,270);Card("nest",730,932,260);
            var hint=Label("物品避让、真实姿态过渡、实际摄入扣库存是固定执行约束。照料不是随机抽奖。",13,"#6F8078");Canvas.SetLeft(hint,440);Canvas.SetTop(hint,320);hint.Width=530;canvas.Children.Add(hint);
        }
        else
        {
            Card("poses",45,25,950,"地面入睡 · 先按权重选姿态，再沿已有素材进入");
            Line(100,105,100,185);Card("rest",45,145,290,"何时进入休息");Card("click",365,145,300);Card("changes",695,145,300);
            Connect(new[]{new Point(180,320),new Point(210,320)},true);Connect(new[]{new Point(370,320),new Point(420,320)},true);
            Connect(new[]{new Point(600,320),new Point(790,320)},true);Connect(new[]{new Point(510,360),new Point(510,405)},true);
            Connect(new[]{new Point(600,340),new Point(660,340),new Point(660,445),new Point(790,445)},true);
            Card("seated",40,280,140);Card("pose-D",210,280,160);Card("pose-A",420,280,180);Card("pose-B",790,280,180);Card("pose-X",420,405,180);Card("pose-M",790,405,180);
            var note=Label("侧躺 / 露肚 / 舒展第三击 → 朝右站起；\n趴卧直接站起、旧侧坐直接站起待补片。",14,"#977045");Canvas.SetLeft(note,45);Canvas.SetTop(note,425);note.Width=340;canvas.Children.Add(note);
            Card("gesture",45,550,950,"呼吸停留 → 等待间隔 → 短动作或换姿态 → 回到呼吸");
            foreach(var (id,x) in new[]{("gesture-D",45d),("gesture-A",290d),("gesture-B",535d),("gesture-X",780d)}){Line(x+35,630,x+35,660);Card(id,x,660,220);}
            Card("wall",45,820,450);Card("nest",525,820,470);
        }
        PaintNodes();
    }
    private void PaintNodes()
    {
        foreach(var (id,border) in nodeBorders)
        {bool active=id==engine.ActiveBehaviorNode||engine.ActiveBehaviorNode=="poses"&&id=="pose-"+engine.RelaxedPose;border.Background=Brush(active?"#E1F2E9":"#FFFFFF");border.BorderBrush=Brush(id==selected?"#247A68":active?"#78AD94":"#D6E1D9");border.BorderThickness=new Thickness(id==selected?2.5:1.5);}
    }
    private void BuildInspector()
    {
        inspector.Children.Clear();fields.Clear();weightLabels.Clear();
        var node=Nodes.FirstOrDefault(n=>n.Id==selected);inspector.Children.Add(Label(node?.Title??"全部可调参数",22));
        var description=Label(node?.Detail??"所有参数按分组列出。时间单位写在输入框右侧；权重在同组内归一化。",12,"#718179");description.Margin=new Thickness(0,10,0,17);inspector.Children.Add(description);
        var parameters=BehaviorSettings.Catalog.Where(p=>selected=="all"||node!.Groups.Contains(p.Group)||selected.StartsWith("pose-")&&p.Key=="pose."+selected[5..]).ToList();
        if(parameters.Count==0)inspector.Children.Add(Label("此节点是固定条件／顺序。请点击下级节点调整对应参数。",13,"#9A7852"));
        foreach(var p in parameters)
        {
            var box=new StackPanel{Margin=new Thickness(0,0,0,18)};inspector.Children.Add(box);box.Children.Add(Label(p.Label,14));
            if(p.Unit=="开关")
            {var toggle=new CheckBox{Content="允许此行为",IsChecked=draft.Get(p.Key)==1,Margin=new Thickness(0,7,0,5)};toggle.Checked+=(_,_)=>{draft.Values[p.Key]=1;dirty=true;status.Text="有未保存的草稿";};toggle.Unchecked+=(_,_)=>{draft.Values[p.Key]=0;dirty=true;status.Text="有未保存的草稿";};box.Children.Add(toggle);box.Children.Add(Label(p.Help,11,"#829087"));continue;}
            var line=new DockPanel{Margin=new Thickness(0,6,0,5)};box.Children.Add(line);var unit=Label(p.Unit,12,"#6E857A");unit.Width=70;unit.Margin=new Thickness(12,6,0,0);DockPanel.SetDock(unit,Dock.Right);line.Children.Add(unit);
            var input=new TextBox{Text=invalid.TryGetValue(p.Key,out var bad)?bad:draft.Get(p.Key).ToString("0.########",CultureInfo.InvariantCulture),FontSize=16,Padding=new Thickness(9,5,9,5),BorderBrush=Brush("#C9D8CE"),Tag=p.Key};fields[p.Key]=input;line.Children.Add(input);
            input.TextChanged+=(_,_)=>{dirty=true;if(double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)){draft.Values[p.Key]=value;invalid.Remove(p.Key);}else invalid[p.Key]=input.Text;status.Text="有未保存的草稿 · 当前运行参数尚未改变";UpdateWeights();};
            var help=Label(p.Help+"  范围 "+p.Min.ToString("0.##")+"～"+p.Max.ToString("0.##"),11,"#829087");box.Children.Add(help);
            if(p.Unit=="权重"){var percent=Label("",12,"#267565");weightLabels[p.Key]=percent;box.Children.Add(percent);}
        }
        if(selected is "food" or "water" or "litter" or "all")
        {if(restartCare.Parent is Panel old)old.Children.Remove(restartCare);inspector.Children.Add(restartCare);inspector.Children.Add(Label("不勾选：保留当前期限，新范围从下一轮使用。勾选：重排三项期限，不补扣库存；当前未补给的吃喝请求可能因需求延后而结束。",11,"#9A7852"));}
        UpdateWeights();PaintNodes();
    }
    private void UpdateWeights()
    {
        foreach(var (key,label) in weightLabels)
        {string group=BehaviorSettings.Catalog.First(p=>p.Key==key).Group;double sum=BehaviorSettings.Catalog.Where(p=>p.Group==group).Sum(p=>Math.Max(0,draft.Get(p.Key)));label.Text=sum>0?$"本组占比  {Math.Max(0,draft.Get(key))/sum:P1}":"本组至少保留一个正权重";}
    }
    private bool Apply()
    {
        try
        {
            if(invalid.Count>0)throw new InvalidDataException("请修正无效数字："+string.Join("、",invalid.Keys.Select(k=>BehaviorSettings.Catalog.First(p=>p.Key==k).Label)));
            draft.Validate();store.Save(draft);engine.ApplyBehaviorSettings(draft,restartCare.IsChecked==true);saveState();dirty=false;restartCare.IsChecked=false;status.Text="已保存并应用 · 当前动作完整衔接，后续决策使用新参数";BuildGraph();RefreshLive();return true;
        }
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException){status.Text="未应用："+e.Message;return false;}
    }
    private void ResetDraft(bool defaults){draft=defaults?new():engine.Settings.Copy();invalid.Clear();restartCare.IsChecked=false;dirty=defaults;BuildInspector();status.Text=defaults?"默认值已载入草稿；保存后才生效。":"已撤销未保存的修改。";}
    private void Import()
    {
        var dialog=new OpenFileDialog{Filter="行为配置 (*.json)|*.json",Title="导入行为参数草稿"};if(dialog.ShowDialog(this)!=true)return;
        try{draft=BehaviorSettingsStore.Parse(File.ReadAllText(dialog.FileName));invalid.Clear();dirty=true;BuildInspector();status.Text="已导入草稿；检查后保存并应用。";}catch(Exception e){status.Text="导入失败，原参数未改变："+e.Message;}
    }
    private void Export()
    {
        try{if(invalid.Count>0)throw new InvalidDataException("请先修正无效输入。");draft.Validate();var dialog=new SaveFileDialog{Filter="行为配置 (*.json)|*.json",FileName="chenpi-behavior.json"};if(dialog.ShowDialog(this)==true){File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(draft,new JsonSerializerOptions{WriteIndented=true}));status.Text="已导出草稿配置。";}}catch(Exception e){status.Text="导出失败："+e.Message;}
    }
    private void RefreshLive()
    {
        string active=engine.ActiveBehaviorNode,name=Nodes.First(n=>n.Id==active).Title.TrimStart('①','②','③','④','⑤','⑥','⑦',' ');
        if(active=="poses")name+=" · "+(engine.RelaxedPose switch{"D"=>"趴卧","A"=>"侧躺","B"=>"露肚","X"=>"舒展","M"=>"掩面",_=>"蜷睡"});
        if(engine.Action.StartsWith("expr-"))name+=" · "+(BehaviorSettings.Catalog.FirstOrDefault(p=>p.Key=="gesture."+engine.Action[5..])?.Label.Replace("权重","")??"互动回应");
        live.Text="● 当前决策："+name;PaintNodes();
        if(prior!=engine.Action){prior=engine.Action;transitions.Enqueue($"{DateTime.Now:HH:mm:ss}  {name}");while(transitions.Count>4)transitions.Dequeue();}
        var due=engine.CareSecondsRemaining;history.Text=string.Join("\n",transitions)+$"\n下次照料：饭 {due["food"]/60:0.#} / 水 {due["water"]/60:0.#} / 砂 {due["litter"]/60:0.#} 分钟";
    }
    internal void RunFixture(string output)
    {
        Directory.CreateDirectory(output);selected="poses";BuildInspector();Capture(System.IO.Path.Combine(output,"poses.png"));
        selected="rest";BuildInspector();fields["sleep.delay"].Text="12";if(!Apply())throw new Exception(status.Text);
        if(store.Load().Get("sleep.delay")!=12||engine.Settings.Get("sleep.delay")!=12)throw new Exception("Editor apply/readback mismatch");
        fields["sleep.delay"].Text="-1";if(Apply()||engine.Settings.Get("sleep.delay")!=12)throw new Exception("Invalid editor value applied");
        ResetDraft(false);selected="food";BuildInspector();fields["food.min"].Text="2";fields["food.max"].Text="2";restartCare.IsChecked=true;if(!Apply())throw new Exception(status.Text);
        if(Math.Abs(engine.CareSecondsRemaining["food"]-120)>.001)throw new Exception("Care reschedule mismatch");
        selected="rest";BuildInspector();Capture(System.IO.Path.Combine(output,"overview.png"));view="sleep";BuildGraph();Fit();selected="gesture-A";BuildInspector();Capture(System.IO.Path.Combine(output,"sleep.png"));
        Width=1000;Height=700;UpdateLayout();Fit();Capture(System.IO.Path.Combine(output,"compact.png"));
        File.WriteAllText(System.IO.Path.Combine(output,"ui-verification.json"),JsonSerializer.Serialize(new{SavedAndReadBack=true,InvalidRejected=true,CareRescheduled=true,ParameterCount=BehaviorSettings.Catalog.Count,Food=engine.State.Food,Water=engine.State.Water,ConfigurationPath=store.PathName}));AppliedInFixture=true;dirty=false;discardOnClose=true;Close();
    }
    private void Capture(string path)
    {
        UpdateLayout();var content=(FrameworkElement)Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);png.Save(stream);
    }
}
