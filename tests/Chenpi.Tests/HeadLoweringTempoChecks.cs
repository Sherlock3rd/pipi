using Chenpi;
using System.Text.Json;

public static class HeadLoweringTempoChecks
{
    public static void Run(Action<bool,string> check,string manifestPath)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(manifestPath));
        var animations=doc.RootElement.GetProperty("animations");
        SpritePlayback Player(){var p=new SpritePlayback();p.Load(doc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);return p;}
        check(doc.RootElement.GetProperty("clips").EnumerateObject().Where(c=>c.Value.TryGetProperty("eatDrinkPlaybackRate",out _)).Select(c=>c.Name).SequenceEqual(new[]{"video-22"}),"only the shared head-lowering clip opts into eating/drinking acceleration");
        foreach(string action in new[]{"eat","drink"})
        {
            var p=Player();var original=p.InspectFrame("video-22",0)!.Value.Definition;
            var intake=p.InspectFrame(action=="eat"?"video-23":"video-25",0)!.Value;
            var finish=p.InspectFrame("video-24",0)!.Value.Definition;
            double lowering=original.Duration/1.5;
            check(Math.Abs(p.ActionDuration(action)!.Value-(lowering+intake.Definition.Duration+finish.Duration))<1e-9,action+" total duration changes only by the shorter preparation");
            p.Sample(action,0);bool unchanged=true;
            for(int i=0;i<original.Count;i++)
            {
                var actual=p.Sample(action,(i+.1)/(original.Fps*1.5))!.Value;
                var source=p.InspectFrame("video-22",i)!.Value.Definition;
                unchanged&=actual.Clip=="video-22"&&actual.Index==i&&actual.Definition.Fps==36&&actual.Definition.Width==source.Width&&actual.Definition.Height==source.Height&&actual.Definition.AnchorX==source.AnchorX&&actual.Definition.AnchorY==source.AnchorY;
            }
            check(unchanged,action+" retains every source frame and all size/contact calibration at 36 fps");
            var before=p.Sample(action,lowering-.00001)!.Value;var after=p.Sample(action,lowering+.00001)!.Value;
            var endLoop=p.Sample(action,lowering+intake.Definition.Duration-.00001)!.Value;
            var raise=p.Sample(action,lowering+intake.Definition.Duration+.00001)!.Value;
            check(before.Index==original.Count-1&&after.Clip==intake.Clip&&after.Index==0&&after.Definition.Fps==24&&endLoop.Index==intake.Definition.Count-1&&raise.Clip=="video-24"&&raise.Index==0&&raise.Definition.Fps==24,action+" keeps complete intake and raising clips at original speed across both seams");
            var state=new PetState{X=400,Y=400,Food=100,Water=100,RestDuration=600,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}};
            var engine=new PetEngine(state,1){FoodSpot=new(400,400),WaterSpot=new(400,400),VisualActionDuration=p.ActionDuration,VisualConsumptionWindow=p.ConsumptionWindow};
            engine.Demo(action);engine.Update(.001,12);
            void Until(double t){while(engine.Action==action&&engine.ActionTime<t)engine.Update(Math.Min(.01,t-engine.ActionTime),12);}
            var window=p.ConsumptionWindow(action)!.Value;
            Until(window.Start-.001);
            check(state.Food==100&&state.Water==100,action+" never deducts stock during accelerated head lowering");
            Until(window.Start+window.Duration/4+.001);
            check((action=="eat"?state.Food:state.Water)==95,action+" first actual intake follows the accelerated preparation timing");
            Until(window.Start+window.Duration+.001);
            check((action=="eat"?state.Food:state.Water)==80&&(action=="eat"?state.Water:state.Food)==100,action+" still consumes exactly one layer from the corresponding bowl");
            Until(p.ActionDuration(action)!.Value-.001);
            check((action=="eat"?state.Food:state.Water)==80,action+" raising the head never consumes extra stock");
            p.Reset();p.Sample(action,0);
            check(p.Sample("drag",1)!.Value.Clip=="video-32",action+" faster lowering remains immediately interruptible");
        }
    }
}
