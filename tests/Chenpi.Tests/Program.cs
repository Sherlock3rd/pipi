using Chenpi;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var s=new PetState();var e=new PetEngine(s);double food=s.Food,water=s.Water;
e.AdvanceNeeds(3600);Check(s.Hunger==38&&s.Thirst==42&&s.Food==food&&s.Water==water,"elapsed awake time grows needs, preserves inventory");
e.Consume(true,4);Check(s.Food==food-4&&s.Hunger==30&&s.Water==water,"food event alone consumes food and restores hunger");
s.Water=2;e.Consume(false,8);Check(s.Water==0&&s.Thirst==37,"partial stock clamps and restores only actual intake");
e.AdvanceNeeds(86400*10);Check(s.Hunger==100&&s.Thirst==100&&s.Water==0,"unattended needs capped, no penalty or negative inventory");
e.Interact();Check(e.Action=="pet","interaction remains available with maximum needs");
e.Refill("water");Check(s.Water==100&&s.Thirst==100,"refill does not directly remove thirst");
e.Nest=new Spot(500,300);e.BeginDrag();e.Drop(true);Check(e.Action=="sleep"&&s.X==500&&s.Sleeping,"drop into nest starts sleep");
e.Interact();Check(e.Action=="wake"&&!s.Sleeping,"sleep can be woken by click");
e.BeginDrag();e.Drop(false);Check(e.Action=="land"&&!s.Sleeping,"outside drop never sleeps");
Check(ClockMath.Elapsed("boot",100,"boot",160)==(60d,false),"same-boot process exit gap counted once");
Check(ClockMath.Elapsed("boot",100,"other",160)==(0d,true),"cross-boot gap flagged, not forged from wall time");
Check(ClockMath.Elapsed("",0,"boot",160)==(0d,false),"first launch does not inherit past uptime");
var c=new PetState{X=100,Y=100,Food=100,Hunger=80};var p=new PetEngine(c){FoodSpot=new Spot(100,100)};
p.Demo("eat");p.Update(.1,12);for(int i=0;i<12;i++)p.Update(.1,12);
Check(c.Food==96,"one completed bite deducted exactly once");p.BeginDrag();for(int i=0;i<50;i++)p.Update(.1,12);Check(c.Food==96,"drag interrupts future bite events");
var t=new PetState{X=100,Y=100,Food=0,Water=0,Hunger=100,Thirst=100,Litter=100};var b=new PetEngine(t);
int notices=0;b.RequestedAttention+=()=>notices++;
b.AdvanceNeeds(301);for(int i=0;i<1200;i++)b.Update(.1,12);
Check(notices<=4&&notices>0,"unmet needs use global reminder cooldown");Check(t.Food==0&&t.Water==0,"begging never consumes absent supplies");
var furniture=new PetState();var home=new PetEngine(furniture);home.Layout(1200,800);
Check(furniture.X>900&&furniture.Y>600&&home.Nest.X-home.LitterSpot.X<350,"first layout places cat and compact home at bottom right");
home.MoveObject("food",new Spot(500,500));home.Layout(1200,800);
Check(home.FoodSpot==new Spot(500,500),"layout refresh preserves freely moved object");
home.Sleep();home.MoveObject("nest",new Spot(800,500));
Check(home.Action=="sleep"&&furniture.X==800&&furniture.Y==500,"moving occupied nest carries sleeping cat");
home.Recall();home.MoveObject("water",new Spot(-999,9999));
Check(home.WaterSpot.X>=48&&home.WaterSpot.Y<=760,"offscreen furniture constrained to visible area");
home.Demo("eat");home.MoveObject("food",new Spot(furniture.X,furniture.Y));home.Update(.1,12);
Check(home.Action=="eat","moving bowl while cat approaches retargets destination");
double beforeFood=furniture.Food;home.MoveObject("food",new Spot(200,300));for(int i=0;i<15;i++)home.Update(.1,12);
Check(furniture.Food==beforeFood&&home.Action=="walk","moving bowl during eating interrupts old consumption");
var remembered=home.FoodSpot;var age=furniture.AdoptedAt;home.Layout(1200,800,true);
Check(home.FoodSpot!=remembered&&furniture.Food==beforeFood&&furniture.AdoptedAt==age,"reset layout preserves care and adoption history");
home.MoveObject("food",new Spot(500,400));home.Layout(900,600);
Check(home.FoodSpot==new Spot(375,300),"resize or scale changes preserve relative furniture positions");
string folder=Path.Combine(Path.GetTempPath(),"Chenpi-tests-"+Guid.NewGuid());
try
{
    var storage=new Store(folder);home.MoveObject("litter",new Spot(420,340));storage.Save(furniture);
    var loaded=storage.Load();Check(loaded.LitterPosition==new Spot(420,340)&&loaded.Food==furniture.Food,"furniture positions and inventory survive save/load");
    storage.Save(furniture);File.WriteAllText(storage.PathName,"broken");loaded=storage.Load();
    Check(loaded.LitterPosition==new Spot(420,340)&&storage.Warning is not null,"corrupt primary recovers object layout from backup");
}
finally{Directory.Delete(folder,true);}
void Advance(PetEngine pet,double seconds){for(int i=0;i<(int)Math.Ceiling(seconds/.1);i++)pet.Update(.1,12);}
var lazyState=new PetState{X=400,Y=300,RestDuration=600};var lazy=new PetEngine(lazyState,41);lazy.Layout(1200,800);
var lazyStart=new Spot(lazyState.X,lazyState.Y);Advance(lazy,290);
Check(new Spot(lazyState.X,lazyState.Y)==lazyStart&&!lazyState.Sleeping,"stationary cat stays in one region instead of roaming every few seconds");
Advance(lazy,11);Check(lazyState.Sleeping&&!lazyState.SleepingInNest&&new Spot(lazyState.X,lazyState.Y)==lazyStart,"five-minute stay sleeps in place, without teleporting to bed");
lazy.MoveObject("nest",new Spot(900,600));lazy.Layout(1200,800);
Check(new Spot(lazyState.X,lazyState.Y)==lazyStart,"ground sleeping cat does not follow a moved nest or layout refresh");
lazy.Interact();Check(lazy.Action=="wake"&&!lazyState.Sleeping,"click wakes ground sleep immediately");
lazy.Interact();lazy.Interact();Check(lazy.Action is "paw" or "roll" or "pet","repeated clicks play interactive poses");
for(int seed=0;seed<50;seed++)
{
 var randomStay=new PetEngine(new PetState(),seed);
 if(randomStay.State.RestDuration<120||randomStay.State.RestDuration>3600)throw new Exception("stay range");
}
Check(true,"sampled stays always fall within two to sixty minutes");
var mover=new PetEngine(new PetState{X=500,Y=500,RestDuration=120,RestElapsed=119},99);mover.Layout(1200,800);Advance(mover,5);
Check(mover.Action=="walk","finished short stay causes one relocation");Advance(mover,10);
Check(mover.Action!="walk"&&mover.State.RestElapsed<15&&mover.State.RestDuration>=120,"arrival draws a fresh long stay once");
var hover=new PetEngine(new PetState{X=500,Y=400,RestDuration=600},1);
for(int i=0;i<49;i++)hover.ObservePointer(.1,true,new Spot(500,330));
Check(hover.Action!="rub","hover under five seconds does not rub");
hover.ObservePointer(.2,true,new Spot(500,330));Check(hover.Action=="rub","continuous five-second hover starts rubbing");
Advance(hover,4.2);hover.ObservePointer(6,true,new Spot(500,330));
Check(hover.Action!="rub","continuous hover does not repeatedly retrigger rub");
hover.ObservePointer(0,false,new Spot());hover.ObservePointer(5.1,true,new Spot(500,330));
Check(hover.Action=="rub","leaving and re-entering rearms hover");
var toy=new PetEngine(new PetState{X=500,Y=400,Food=80,Water=80,RestDuration=600},1);
Check(PetEngine.CatPlayRadius==78*5&&PetEngine.ToyPlayRadius==34*5,"outer detection radii preserve the fivefold range");
var center=toy.CatPlayCenter;toy.SetToy(true,new Spot(center.X+PetEngine.CatPlayRadius+PetEngine.ToyPlayRadius+.1,center.Y));toy.Update(.1,12);
Check(!toy.ToyOverlaps&&!toy.Action.StartsWith("toy-"),"separated toy and cat circles do not trigger play");
double beforeChaseX=toy.State.X;
toy.SetToy(true,new Spot(center.X+PetEngine.CatPlayRadius+PetEngine.ToyPlayRadius,center.Y));toy.Update(.1,12);
Check(toy.ToyOverlaps&&toy.Action=="toy-run"&&toy.State.X>beforeChaseX,"outer edge immediately runs toward target without observation delay");
toy.SetToy(true,toy.CatPlayCenter);Advance(toy,.6);Check(toy.Action=="toy-bat","inner range immediately switches to close grabbing");
toy.SetToy(false,new Spot());Check(!toy.ToyHeld&&toy.Action=="sit"&&toy.State.Food==80&&toy.State.Water==80,"putting toy away cancels play without changing inventory");
toy.Sleep();toy.SetToy(true,toy.CatPlayCenter);toy.Update(.1,12);
Check(toy.Action=="toy-bat"&&!toy.State.Sleeping,"near toy can wake sleeping cat directly into grabbing");
var chase=new PetEngine(new PetState{X=600,Y=450,RestDuration=600},2);chase.Layout(1400,900);
chase.SetToy(true,new Spot(900,385));chase.Update(.1,12);
Check(chase.Action=="toy-run"&&!chase.ToyWithinCatchRange&&chase.State.X==624,"far toy in outer range uses run speed immediately");
Advance(chase,2);
Check(chase.Action=="toy-bat"&&chase.ToyWithinCatchRange,"running automatically reaches inner range and grabs");
var grabbedAt=new Spot(chase.State.X,chase.State.Y);Advance(chase,2);
Check(chase.Action=="toy-bat"&&new Spot(chase.State.X,chase.State.Y)==grabbedAt,"close grabbing stays put instead of timed run and pause cycling");
chase.SetToy(true,new Spot(chase.State.X-250,chase.CatPlayCenter.Y));chase.Update(.1,12);
Check(chase.Action=="toy-run"&&chase.FacingLeft&&chase.State.X<grabbedAt.X,"moving feather outside inner range immediately resumes chase in new direction");
var beforeOutside=new Spot(chase.State.X,chase.State.Y);chase.SetToy(true,new Spot(-1000,-1000));chase.Update(.1,12);
Check(chase.Action=="sit"&&new Spot(chase.State.X,chase.State.Y)==beforeOutside,"leaving outer range stops chasing immediately");
chase.SetToy(true,new Spot(chase.CatPlayCenter.X+PetEngine.CatCatchRadius,chase.CatPlayCenter.Y));chase.Update(.1,12);
Check(chase.Action=="toy-bat","inner circle boundary counts as close range");
var innerStart=new Spot(chase.State.X,chase.State.Y);
chase.SetToy(true,new Spot(chase.CatPlayCenter.X+200,chase.CatPlayCenter.Y));chase.Update(.1,12);
Check(chase.Action=="toy-run"&&chase.State.X>innerStart.X,"large feather attraction radius cannot cause distant grabbing");
string queuedFolder=Path.Combine(Path.GetTempPath(),"Chenpi-queued-"+Guid.NewGuid());
try
{
 var queuedStore=new Store(queuedFolder);var queuedState=new PetState{Food=61};queuedStore.QueueSave(queuedState);queuedState.Food=23;queuedStore.QueueSave(queuedState);queuedStore.Flush();
 Check(queuedStore.Load().Food==23&&queuedStore.WriteError is null,"background writes preserve snapshots in order and flush on exit");
}
finally{Directory.Delete(queuedFolder,true);}
for(int seed=0;seed<100;seed++)
{
 var clocks=new PetState{TotalSeconds=12345};_ =new PetEngine(clocks,seed);
 if(clocks.WaterClock.NextDue-12345 is <600 or >1800||clocks.FoodClock.NextDue-12345 is <1200 or >3600||clocks.LitterClock.NextDue-12345 is <3600 or >7200)throw new Exception("care interval bounds");
}
Check(true,"independent care intervals sample the required three ranges");
var timedState=new PetState{X=500,Y=400,Water=80,Food=80,RestDuration=600,WaterClock=new(){NextDue=600},FoodClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}};
var timed=new PetEngine(timedState,42){WaterSpot=new Spot(500,400)};
timed.AdvanceNeeds(599);timed.Update(.1,12);
Check(timed.Action!="drink"&&timedState.Water==80&&timedState.WaterClock.NextDue==600,"no premature drink or per-frame timer redraw");
timed.AdvanceNeeds(1);timed.Update(.1,12);
Check(timed.Action=="drink"&&timedState.Water==80,"due drink starts by reaching bowl and does not consume on arrival");
Advance(timed,1.2);double nextDrink=timedState.WaterClock.NextDue;
Check(timedState.Water==76&&nextDrink>=1200&&nextDrink<=2400&&timedState.FoodClock.NextDue==99999,"first actual sip resamples only its own timer");
Advance(timed,3);Check(timedState.WaterClock.NextDue==nextDrink,"later sips in same visit do not redraw timer");
var unhurriedState=new PetState{X=500,Y=400,Hunger=100,Thirst=100,Bladder=100,RestDuration=600,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}};
var unhurried=new PetEngine(unhurriedState,3);Advance(unhurried,10);
Check(unhurriedState.Food==70&&unhurriedState.Water==75&&unhurriedState.Litter==15,"old hunger thresholds no longer override random visit intervals");
var offlineState=new PetState{X=300,Y=300,Food=80,Water=80,Litter=0,FoodClock=new(){NextDue=2},WaterClock=new(){NextDue=1},LitterClock=new(){NextDue=3}};
var offline=new PetEngine(offlineState,7){FoodSpot=new Spot(300,300),WaterSpot=new Spot(300,300),LitterSpot=new Spot(300,300)};
offline.AdvanceNeeds(86400);
Check(offlineState.Food==80&&offlineState.Water==80&&offlineState.Litter==0,"offline overdue schedules do not replay consumption or toileting");
Advance(offline,30);
Check(offlineState.Food==64&&offlineState.Water==64&&offlineState.Litter==28,"long overdue gap leads to one real visit of each type, not a backlog");
Check(offlineState.LitterClock.NextDue-86400 is >=3600 and <=7200,"completed toilet resamples sixty to one hundred twenty minutes");
var requestState=new PetState{X=300,Y=300,Water=0,RestDuration=600};var requester=new PetEngine(requestState,9);requester.Layout(1400,900);
requester.AdvanceNeeds(299);requester.Update(.1,12);
Check(requestState.CareRequest is null,"empty resource waits a full five awake minutes before requesting");
requester.AdvanceNeeds(1);requester.Update(.1,12);
Check(requestState.CareRequest=="water"&&requester.Action=="request-walk","five-minute empty resource initiates bottom-center request walk");
Advance(requester,15);
Check(requester.Action=="request-water"&&new Spot(requestState.X,requestState.Y)==requester.RequestSpot,"requesting cat stands at screen bottom center");
requester.ObservePointer(.01,true,requester.CatPlayCenter);
Check(requestState.Guiding&&requester.Action=="guide-walk","hover immediately overrides request with guidance, without five-second rub delay");
requester.ObservePointer(.1,false,new Spot(-1000,-1000));var waitingAt=new Spot(requestState.X,requestState.Y);Advance(requester,1);
Check(requester.Action=="guide-look"&&new Spot(requestState.X,requestState.Y)==waitingAt,"guide looks back and waits when pointer does not follow");
int lookBacks=0;
for(int i=0;i<1500&&requester.Action!="guide-water";i++){requester.ObservePointer(.1,false,requester.CatPlayCenter);requester.Update(.1,12);if(requester.Action=="guide-look")lookBacks++;}
Check(requester.Action=="guide-water"&&lookBacks>0&&new Spot(requestState.X,requestState.Y)==requester.GuideDestination("water"),"following pointer leads to bowl with look-back pauses and stops beside it");
var oldGoal=requester.GuideDestination("water");requester.MoveObject("water",new Spot(400,600));
for(int i=0;i<2000&&(requester.Action!="guide-water"||new Spot(requestState.X,requestState.Y).Distance(requester.GuideDestination("water"))>2);i++){requester.ObservePointer(.1,false,requester.CatPlayCenter);requester.Update(.1,12);}
Check(requester.Action=="guide-water"&&new Spot(requestState.X,requestState.Y)!=oldGoal,"moving the requested bowl retargets the guide");
double stillThirsty=requestState.Thirst;requester.Refill("water");
Check(requestState.CareRequest is null&&!requestState.Guiding&&requestState.Water==100&&requestState.Thirst==stillThirsty&&requestState.WaterClock.UnavailableSince is null,"refill ends guidance and clears shortage timer without directly relieving thirst");
var partialLitter=new PetEngine(new PetState{X=500,Y=400,Litter=99,RestDuration=600,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}},1);
partialLitter.AdvanceNeeds(301);partialLitter.Update(.1,12);
Check(partialLitter.State.CareRequest is null,"litter requests only when full, not at the old partial threshold");
partialLitter.State.Litter=100;partialLitter.AdvanceNeeds(300);partialLitter.Update(.1,12);
Check(partialLitter.State.CareRequest=="litter","full litter after five minutes requests cleaning");
partialLitter.Refill("litter");Check(partialLitter.State.CareRequest is null&&partialLitter.State.LitterClock.UnavailableSince is null,"early remote cleaning cancels request before arrival");
var multi=new PetEngine(new PetState{X=500,Y=400,Food=0,Water=0,Litter=100},4);multi.Layout(1400,900);multi.AdvanceNeeds(300);multi.Update(.1,12);
Check(multi.State.CareRequest=="water","multiple shortages select one request at a time");
multi.Refill("water");Advance(multi,1.2);Check(multi.State.CareRequest=="food","resolving one shortage advances to the next unmet request");
string careFolder=Path.Combine(Path.GetTempPath(),"Chenpi-care-"+Guid.NewGuid());
try
{
 var careStore=new Store(careFolder);careStore.Save(multi.State);double foodDue=multi.State.FoodClock.NextDue;double? emptySince=multi.State.FoodClock.UnavailableSince;
 var restored=new PetEngine(careStore.Load(),999);
 Check(restored.State.FoodClock.NextDue==foodDue&&restored.State.FoodClock.UnavailableSince==emptySince&&restored.State.CareRequest=="food","care schedule and pending shortage survive save and new random seed");
}
finally{Directory.Delete(careFolder,true);}
var movedTray=new PetEngine(new PetState{X=400,Y=400,Litter=0},8);movedTray.Layout(1200,800);
movedTray.State.X=movedTray.LitterSpot.X;movedTray.State.Y=movedTray.LitterSpot.Y;
movedTray.Demo("toilet");Advance(movedTray,4.3);
Check(movedTray.Action=="bury"&&movedTray.State.Litter==28,"toileting commits once before burial");
double toiletDue=movedTray.State.LitterClock.NextDue;
movedTray.MoveObject("litter",new Spot(500,500));Advance(movedTray,30);
Check(movedTray.State.Litter==28&&movedTray.State.LitterClock.NextDue==toiletDue,"moving litter during burial does not repeat toilet or resample its clock");
using var variantDoc=System.Text.Json.JsonDocument.Parse("""
{"variants":[
 {"id":"pet-a","group":"click","baseAction":"pet","enabled":true,"fps":12},
 {"id":"pet-b","group":"click","baseAction":"pet","enabled":true},
 {"id":"reserved","group":"click","baseAction":"pet","enabled":false},
 {"id":"missing","group":"click","baseAction":"pet","enabled":true},
 {"id":"invalid","group":"click","baseAction":"pet","enabled":true,"fps":0},
 {"id":"bad","group":"click","baseAction":"pet","enabled":"unfinished"},
 {"id":"wrong-group","group":"idle","baseAction":"pet","enabled":true},
 {"id":"idle-a","group":"idle","baseAction":"sit","enabled":true,"loop":false},
 {"id":"forbidden","group":"click","baseAction":"eat","enabled":true}
]}
""");
var variantPool=new AnimationVariants(5);variantPool.Load(variantDoc.RootElement,id=>id!="missing");
string? previousVariant=null;bool healthyPool=true;
for(int i=0;i<100;i++){var chosen=variantPool.Choose("pet");healthyPool&=chosen?.Id is "pet-a" or "pet-b"&&chosen.Id!=previousVariant;previousVariant=chosen?.Id;}
Check(healthyPool,"only enabled complete valid variants enter pool, without consecutive repeats");
Check(variantPool.Choose("sit") is {Id:"idle-a",Loop:false},"idle variants remain separate and keep playback options");
Check(variantPool.Choose("eat") is null&&variantPool.Choose("paw") is null,"unconfigured and gameplay actions fall back without variant substitution");
using var legacyManifest=System.Text.Json.JsonDocument.Parse("{\"animations\":{}}");
variantPool.Load(legacyManifest.RootElement,_=>true);
Check(variantPool.Choose("pet") is null,"legacy manifest without variants preserves original presentation");
var sameClick=new PetEngine(new PetState(),1);sameClick.Interact();long firstRevision=sameClick.ActionRevision;
sameClick.Update(.1,12);Check(sameClick.ActionRevision==firstRevision,"render updates do not redraw animation selection");
sameClick.Interact();Check(sameClick.ActionRevision>firstRevision,"each new action exposes one selection boundary");
Console.WriteLine($"{checks} checks passed.");
