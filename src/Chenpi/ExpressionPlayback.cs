using System;
using System.Collections.Generic;

namespace Chenpi;

public sealed partial class SpritePlayback
{
    // X is the new stretched pose; C remains the original curled sleep pose.
    public bool HasExpressions=>clips.ContainsKey("video-98")&&clips.ContainsKey("video-58");
    public bool HasCompletion {get;private set;}
    private static readonly (string From,string To,string Clip)[] completionEdges={
        ("I","SR","99"),("I","SL","100"),("SR","D","101"),("SL","D","102"),
        ("D","SR","103"),("D","SL","104"),("A","SL","105"),("B","SL","106"),("X","SL","107"),
        ("SR","RR","108"),("RR","SR","110"),("SL","RL","111"),("RL","SL","113"),
        ("SR","SF","114"),("SL","SF","115"),("SF","F","118"),
        ("C","D","121"),("D","C","122"),("C","A","123"),("A","C","124")};
    private static readonly (string From,string To,string Clip)[] expressionEdges={
        ("SR","HR","52"),("HR","SR","54"),("SL","HL","55"),("HL","SL","57"),
        ("F","D","58"),("D","F","59"),("D","A","60"),("A","D","61"),
        ("A","B","62"),("B","A","63"),("A","X","64"),("X","A","65"),
        ("A","SR","70"),("B","SR","74"),("X","SR","78"),("A","M","83"),("M","A","85")};
    public static string ExpressionPose(int id)=>id switch {
        53=>"HR",56=>"HL",66 or 79 or 89 or 95=>"D",
        67 or 68 or 69 or 80 or 90 or 96=>"A",
        71 or 72 or 73 or 81 or 91 or 97=>"B",
        75 or 76 or 77 or 82 or 92 or 98=>"X",84=>"M",86=>"WR",87=>"WL",88=>"F",93 or 119=>"SR",94 or 120=>"SL",116 or 117=>"SF",_=>""};
    private static string PoseLoop(string p)=>p switch {"H_D"=>"126","H_A"=>"129","H_B"=>"132","H_X"=>"135","H_M"=>"138","D"=>"66","A"=>"67","B"=>"71","X"=>"75","M"=>"84","HR"=>"53","HL"=>"56",_=>""};
    public bool WallRestComplete(string target,double now,double notBefore)
    {
        string loop="video-"+PoseLoop(target);
        if(current!=loop||!clips.TryGetValue(loop,out var clip))return false;
        double end=started+Math.Max(1,Math.Ceiling((notBefore-started)/clip.Duration))*clip.Duration;
        return now+1e-8>=end;
    }
    public void RestorePose(string p){Reset();pose=destination=p;group=p;}
    private IEnumerable<(string From,string To,string Clip)> GraphEdges()
    {foreach(var e in edges)yield return e;if(HasCompletion){foreach(var e in completionEdges)yield return e;foreach(var e in CarryEdges())yield return e;}if(HasExpressions)foreach(var e in expressionEdges)yield return e;}
    public bool PreparePose(string target,double now)
    {
        if(!HasExpressions)return true;
        Sample("rest-"+target,now);
        return pose==target&&destination==target&&(current.Length==0||clips[current].Loop);
    }
    // Without authored lying-to-pickup clips, preserve the current real pose
    // through immediate dragging/drop instead of snapping to front seated art.
    private SpriteFrame? carriedExpression;
    public bool CarriesExpression=>carriedExpression is not null;
    private string carriedPose="";
    private double carriedAt;
    private string? walkVocalClip;
    private int travelLegs;
    public Func<int>? WalkingCallEvery {get;set;}
    private string WalkingLoop(string standingPose)
    {
        if(walkVocalClip is string vocal){walkVocalClip=null;return vocal;}
        return standingPose=="WL"?"video-14":"video-right";
    }
    private SpriteFrame? SampleCarriedExpression(string action,double now)
    {
        if(action=="drag"&&carriedExpression is null&&lastSample is SpriteFrame previous&&HasExpressions&&
           (PoseLoop(pose).Length>0||PoseLoop(destination).Length>0))
        {carriedExpression=previous;carriedPose=pose;carriedAt=now;}
        if(carriedExpression is SpriteFrame carried)
        {
            if(action is "drag" or "land")return carried;
            started+=now-carriedAt;careStarted+=now-carriedAt;carriedExpression=null;
        }
        return null;
    }
}
