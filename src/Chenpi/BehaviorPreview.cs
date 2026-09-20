using System;

namespace Chenpi;

public sealed partial class PetEngine
{
    private string? debugPose;
    private int debugExpression;
    private double debugReadyAt=-1;
    public bool DebugBehaviorActive=>debugPose is not null;
    private double WallGoal(bool left)=>left?-WallLeftOffset:Width-WallRightOffset;
    public string WallBlockReason(bool left)
    {
        if(!ExpressionsEnabled)return "扶墙素材尚未加载";
        double x=WallGoal(left);
        if(x<0||x>Width)return "当前屏幕宽度不足";
        foreach(var item in RestObstacles())if(Math.Abs(x-item.X)<item.Radius)return "这一侧有家具占用，请先移开附近物品";
        return "";
    }
    public string WallStatus
    {
        get
        {
            return $"扶墙与普通移动目的地同级，权重 {Settings.Get("move.wall"):0.##}；{(Settings.Get("wall.enabled")==0?"自主扶墙关闭":"不限制距离与本次启动次数")}。左侧：{(WallBlockReason(true) is string l&&l.Length>0?l:"可执行")}；右侧：{(WallBlockReason(false) is string r&&r.Length>0?r:"可执行")}。";
        }
    }
    public string ExecuteBehavior(string id)
    {
        if(id is "food" or "water" or "litter" or "nest")
        {
            if(id=="food"&&State.Food<=0)return "食盆为空，请先加粮";
            if(id=="water"&&State.Water<=0)return "水盆为空，请先加水";
            if(id=="litter"&&State.Litter>=100)return "猫砂已满，请先清理";
            Demo(id switch {"food"=>"eat","water"=>"drink","litter"=>"toilet",_=>"sleep"});return "已执行；照料会实际消耗库存";
        }
        if(id is "click" or "input"){Interact();return "已执行一次点击互动";}
        if(id=="movement"||id.StartsWith("move-"))
        {
            var choices=MovementChoices();
            if(!System.Linq.Enumerable.Any(choices,c=>id=="movement"||c.Id==id))return "没有符合权重和空位条件的移动目的地";
            BeginManualSequence();
            if(StartMovementChoice(id=="movement"?null:id))
            {
                if(arrival is "rest-HL" or "rest-HR"){debugPose=arrival[5..];debugReadyAt=-1;}
                return "按同级目的地权重选择："+LastMovementChoice+"；沿真实动作移动";
            }
            FinishManualSequence();return "没有可用目的地";
        }
        if(id=="gesture")id="gesture-"+(RelaxedPose is "D" or "A" or "B" or "X"?RelaxedPose:"D");
        if(id=="changes")id="pose-"+(RelaxedPose switch {"D"=>"A","A"=>Settings.Choose(random,"change.B","change.X","change.M")[7..],"B" or "X" or "M"=>"A",_=>"D"});
        string pose="";int expression=0;
        if(id=="wall")id=State.X<Width/2?"wall-left":"wall-right";
        if(id is "wall-left" or "wall-right")
        {
            bool left=id=="wall-left";string blocked=WallBlockReason(left);if(blocked.Length>0)return blocked;
            pose=left?"HL":"HR";
        }
        else if(id.StartsWith("pose-"))pose=id[5..];
        else if(id is "rest" or "poses")pose=Settings.Choose(random,"pose.D","pose.A","pose.B","pose.X","pose.M")[5..];
        else if(id=="seated")pose="F";
        else if(id.StartsWith("gesture-"))
        {
            int[] ids=id switch {"gesture-D"=>new[]{79,89,95},"gesture-A"=>new[]{80,90,96},"gesture-B"=>new[]{81,91,97},"gesture-X"=>new[]{82,92,98},_=>Array.Empty<int>()};
            if(ids.Length>0)expression=int.Parse(Settings.Choose(random,"gesture."+ids[0],"gesture."+ids[1],"gesture."+ids[2])[8..]);
        }
        else if(id.StartsWith("expr-")&&int.TryParse(id[5..],out int clip)&&clip is 68 or 69 or 72 or 73 or 76 or 77 or 79 or 80 or 81 or 82 or 88 or 89 or 90 or 91 or 92 or 93 or 94 or 95 or 96 or 97 or 98)expression=clip;
        if(expression>0)pose=SpritePlayback.ExpressionPose(expression);
        if(pose is not ("D" or "A" or "B" or "X" or "M" or "HR" or "HL" or "F" or "SR" or "SL"))
            return id=="startup"||id.StartsWith("intro-")?"开场尚未启用，等待独立跑步等素材补齐":"这是条件／组合节点，请双击具体动作节点";
        if(!ExpressionsEnabled)return "动作素材尚未加载";
        BeginManualSequence();NewRest();pendingReaction=0;RelaxedClickCount=0;clickWindow=-1;
        debugPose=pose;debugExpression=expression;debugReadyAt=-1;
        var targetSpot=pose is "HL" or "HR"?new Spot(WallGoal(pose=="HL"),GroundY):NearestRestSpot(new(State.X,GroundY));
        if(Math.Abs(targetSpot.X-State.X)>2||State.SleepingInNest)Go(targetSpot,"rest-"+pose,"工作台：前往动作位置");
        else RestInPose(pose);
        return "已切换：先完成起身／移动／姿态过渡，再播放所选动作";
    }
    private void CancelDebugBehavior()
    {if(DebugBehaviorActive){debugPose=null;debugExpression=0;manualSequence=false;}}
    private bool UpdateDebugBehavior()
    {
        if(debugPose is not string pose||Action=="walk")return false;
        if(!Action.StartsWith("rest-")&&!Action.StartsWith("expr-")){CancelDebugBehavior();return false;}
        if(Action.StartsWith("expr-"))
        {
            if(ActionTime<duration)return true;
            debugExpression=0;debugReadyAt=-1;RestInPose(pose);return true;
        }
        if(VisualPoseReady?.Invoke(pose,Now)==false)return true;
        if(debugExpression>0){SetAction("expr-"+debugExpression,4,"工作台：执行所选动作");return true;}
        if(debugReadyAt<0)debugReadyAt=Now;
        if(Now-debugReadyAt<(pose is "HL" or "HR"?Settings.Get("wall.duration"):1))return true;
        if(pose is "HL" or "HR"&&VisualWallRestComplete?.Invoke(pose,Now,debugReadyAt+Settings.Get("wall.duration"))==false)return true;
        debugPose=null;manualSequence=false;sleepGrace=Now+Settings.Get("sleep.grace");
        if(LyingPose(pose)){RestInPose(pose);return true;}
        FinishManualSequence();return true;
    }
}
