using Chenpi;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var s=new PetState();var e=new PetEngine(s);double food=s.Food,water=s.Water;
e.AdvanceNeeds(3600);Check(s.Hunger==38&&s.Thirst==42&&s.Food==food&&s.Water==water,"elapsed awake time grows needs, preserves inventory");
e.Consume(true,4);Check(s.Food==food-4&&s.Hunger==30&&s.Water==water,"food event alone consumes food and restores hunger");
s.Water=2;e.Consume(false,8);Check(s.Water==0&&s.Thirst==37,"partial stock clamps and restores only actual intake");
e.AdvanceNeeds(86400*10);Check(s.Hunger==100&&s.Thirst==100&&s.Water==0,"unattended needs capped, no penalty or negative inventory");
e.Interact();Check(e.Action=="pet","interaction remains available with maximum needs");
e.Refill("water");Check(s.Water==20&&s.Thirst==100,"refill does not directly remove thirst");
e.Nest=new Spot(500,300);e.BeginDrag();e.Drop(true);Check(e.Action=="sleep"&&s.X==500&&s.Sleeping,"drop into nest starts sleep");
e.Interact();Check(e.Action=="wake"&&!s.Sleeping,"sleep can be woken by click");
e.BeginDrag();e.Drop(false);Check(e.Action=="land"&&!s.Sleeping,"outside drop never sleeps");
Check(ClockMath.Elapsed("boot",100,"boot",160)==(60d,false),"same-boot process exit gap counted once");
Check(ClockMath.Elapsed("boot",100,"other",160)==(0d,true),"cross-boot gap flagged, not forged from wall time");
Check(ClockMath.Elapsed("",0,"boot",160)==(0d,false),"first launch does not inherit past uptime");
var c=new PetState{X=100,Y=100,Food=100,Hunger=80};var p=new PetEngine(c){FoodSpot=new Spot(100,100)};
p.Demo("eat");p.Update(.1,12);for(int i=0;i<12;i++)p.Update(.1,12);
Check(c.Food==95,"one completed bite deducted exactly once");p.BeginDrag();for(int i=0;i<50;i++)p.Update(.1,12);Check(c.Food==95,"drag interrupts future bite events");
var t=new PetState{X=100,Y=100,Food=0,Water=0,Hunger=100,Thirst=100,Litter=100};var b=new PetEngine(t);
int notices=0;b.RequestedAttention+=()=>notices++;
b.AdvanceNeeds(301);for(int i=0;i<1200;i++)b.Update(.1,12);
Check(notices<=4&&notices>0,"unmet needs use global reminder cooldown");Check(t.Food==0&&t.Water==0,"begging never consumes absent supplies");
var furniture=new PetState();var home=new PetEngine(furniture);home.Layout(1200,800);
Check(furniture.X>900&&furniture.Y>600&&home.Nest.X-InteractionGeometry.NestHalfWidth>home.FoodSpot.X+36&&home.WaterSpot.X-36>home.LitterSpot.X+InteractionGeometry.LitterHalfWidth,"first layout places cat and compact home at bottom right");
home.MoveObject("food",new Spot(500,500));home.Layout(1200,800);
Check(home.FoodSpot==new Spot(500,home.GroundY),"layout refresh preserves freely moved object");
home.Sleep();home.MoveObject("nest",new Spot(800,500));
Check(home.Action=="sleep"&&furniture.X==800&&furniture.Y==home.GroundY,"moving occupied nest carries sleeping cat");
home.Recall();home.MoveObject("water",new Spot(-999,9999));
Check(home.WaterSpot.X>=48&&home.WaterSpot.Y<=760,"offscreen furniture constrained to visible area");
home.Demo("eat");home.MoveObject("food",new Spot(furniture.X+InteractionGeometry.MouthOffsetX,furniture.Y));home.Update(.1,12);
Check(home.Action=="eat","moving bowl while cat approaches retargets destination");
double beforeFood=furniture.Food;home.MoveObject("food",new Spot(200,300));for(int i=0;i<15;i++)home.Update(.1,12);
Check(furniture.Food==beforeFood&&home.Action=="walk","moving bowl during eating interrupts old consumption");
var remembered=home.FoodSpot;var age=furniture.AdoptedAt;home.Layout(1200,800,true);
Check(home.FoodSpot!=remembered&&furniture.Food==beforeFood&&furniture.AdoptedAt==age,"reset layout preserves care and adoption history");
home.MoveObject("food",new Spot(500,400));home.Layout(900,600);
Check(home.FoodSpot==new Spot(375,home.GroundY),"resize or scale changes preserve relative furniture positions");
string folder=Path.Combine(Path.GetTempPath(),"Chenpi-tests-"+Guid.NewGuid());
try
{
    var storage=new Store(folder);home.MoveObject("litter",new Spot(420,340));storage.Save(furniture);
    var loaded=storage.Load();Check(loaded.LitterPosition==new Spot(420,home.GroundY)&&loaded.Food==furniture.Food,"furniture positions and inventory survive save/load");
    storage.Save(furniture);File.WriteAllText(storage.PathName,"broken");loaded=storage.Load();
    Check(loaded.LitterPosition==new Spot(420,home.GroundY)&&storage.Warning is not null,"corrupt primary recovers object layout from backup");
}
finally{Directory.Delete(folder,true);}
void Advance(PetEngine pet,double seconds){for(int i=0;i<(int)Math.Ceiling(seconds/.1);i++)pet.Update(.1,12);}
var lazyState=new PetState{X=400,Y=300,RestDuration=600};var lazy=new PetEngine(lazyState,41);lazy.Layout(1200,800);
lazyState.X=lazy.NearestRestSpot(new(lazyState.X,lazy.GroundY)).X;
var lazyStart=new Spot(lazyState.X,lazyState.Y);Advance(lazy,50);
Check(new Spot(lazyState.X,lazyState.Y)==lazyStart&&!lazyState.Sleeping,"stationary cat stays in one region instead of roaming every few seconds");
Advance(lazy,11);Check(lazyState.Sleeping&&!lazyState.SleepingInNest&&new Spot(lazyState.X,lazyState.Y)==lazyStart,"one-minute quiet stay sleeps in place, without teleporting to bed");
lazy.MoveObject("nest",new Spot(900,600));lazy.Layout(1200,800);
Check(new Spot(lazyState.X,lazyState.Y)==lazyStart,"ground sleeping cat does not follow a moved nest or layout refresh");
lazy.Interact();Check(lazy.Action=="wake"&&!lazyState.Sleeping,"click wakes ground sleep immediately");
lazy.Interact();lazy.Interact();Check(lazy.Action is "paw" or "roll" or "pet","repeated clicks play interactive poses");
for(int seed=0;seed<50;seed++)
{
 var randomStay=new PetEngine(new PetState(),seed);
 if(randomStay.State.RestDuration<1200||randomStay.State.RestDuration>3600)throw new Exception("stay range");
}
Check(true,"sampled stays always fall within twenty to sixty minutes");
var mover=new PetEngine(new PetState{X=250,Y=500,RestDuration=120,RestElapsed=119},99);mover.Layout(1200,800);Advance(mover,5);
Check(mover.Action=="sleep","legacy short stay settles to sleep instead of forcing relocation");Advance(mover,10);
Check(mover.Action!="walk"&&mover.State.RestElapsed<135&&mover.State.RestDuration>=120,"rest expiry never forces a sleeping cat to roam");
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
var chase=new PetEngine(new PetState{X=600,Y=450,RestDuration=600},2);chase.Layout(2000,900); // Keep this chase fixture away from furniture.
chase.SetToy(true,new Spot(900,chase.CatPlayCenter.Y));chase.Update(.1,12);
Check(chase.Action=="toy-run"&&!chase.ToyWithinCatchRange&&Math.Abs(chase.State.X-(600+PetEngine.ToyRunSpeed*.1))<.0001,"far toy in outer range uses run speed immediately");
Advance(chase,11);
Check(chase.Action=="toy-bat"&&chase.ToyWithinCatchRange,"running automatically reaches inner range and grabs");
var grabbedAt=new Spot(chase.State.X,chase.State.Y);Advance(chase,11);
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
Check(timedState.Water==75&&nextDrink>=1200&&nextDrink<=2400&&timedState.FoodClock.NextDue==99999,"first actual sip resamples only its own timer");
Advance(timed,3);Check(timedState.WaterClock.NextDue==nextDrink,"later sips in same visit do not redraw timer");
var unhurriedState=new PetState{X=500,Y=400,Hunger=100,Thirst=100,Bladder=100,RestDuration=600,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}};
var unhurried=new PetEngine(unhurriedState,3);Advance(unhurried,10);
Check(unhurriedState.Food==70&&unhurriedState.Water==75&&unhurriedState.Litter==15,"old hunger thresholds no longer override random visit intervals");
var offlineState=new PetState{X=300,Y=300,Food=80,Water=80,Litter=0,FoodClock=new(){NextDue=2},WaterClock=new(){NextDue=1},LitterClock=new(){NextDue=3}};
var offline=new PetEngine(offlineState,7){FoodSpot=new Spot(300,300),WaterSpot=new Spot(300,300),LitterSpot=new Spot(300,300)};
offline.AdvanceNeeds(86400);
Check(offlineState.Food==80&&offlineState.Water==80&&offlineState.Litter==0,"offline overdue schedules do not replay consumption or toileting");
Advance(offline,30);
Check(offlineState.Food==60&&offlineState.Water==60&&offlineState.Litter==20,"long overdue gap leads to one real visit of each type, not a backlog");
Check(offlineState.LitterClock.NextDue-86400 is >=3600 and <=7200,"completed toilet resamples sixty to one hundred twenty minutes");
var requestState=new PetState{X=300,Y=300,Water=0,RestDuration=600,LitterPosition=new Spot(1100,852)};var requester=new PetEngine(requestState,9);requester.Layout(1400,900);
requester.AdvanceNeeds(299);requester.Update(.1,12);
Check(requestState.CareRequest is null,"empty resource waits a full five awake minutes before requesting");
requester.AdvanceNeeds(1);requester.Update(.1,12);
Check(requestState.CareRequest=="water"&&requester.Action=="request-walk","five-minute empty resource initiates bottom-center request walk");
Advance(requester,new Spot(requestState.X,requestState.Y).Distance(requester.RequestSpot)/PetEngine.WalkSpeed+1);
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
Check(requestState.CareRequest is null&&!requestState.Guiding&&requestState.Water==20&&requestState.Thirst==stillThirsty&&requestState.WaterClock.UnavailableSince is null,"refill ends guidance and clears shortage timer without directly relieving thirst");
var partialLitter=new PetEngine(new PetState{X=500,Y=400,Litter=99,RestDuration=600,FoodClock=new(){NextDue=99999},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}},1);
partialLitter.AdvanceNeeds(301);partialLitter.Update(.1,12);
Check(partialLitter.State.CareRequest is null,"litter requests only when full, not at the old partial threshold");
partialLitter.State.Litter=100;partialLitter.AdvanceNeeds(300);partialLitter.Update(.1,12);
Check(partialLitter.State.CareRequest=="litter","full litter after five minutes requests cleaning");
partialLitter.Refill("litter");Check(partialLitter.State.CareRequest is null&&partialLitter.State.LitterClock.UnavailableSince is null,"early remote cleaning cancels request before arrival");
var multi=new PetEngine(new PetState{X=350,Y=400,Food=0,Water=0,Litter=100},4);multi.Layout(1400,900);multi.AdvanceNeeds(300);multi.Update(.1,12);
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
movedTray.State.X=movedTray.CareDestination(movedTray.LitterSpot,"toilet").X;movedTray.State.Y=movedTray.LitterSpot.Y;
movedTray.Demo("toilet");Advance(movedTray,4.3);
Check(movedTray.Action=="walk"&&movedTray.State.Litter==20,"toileting commits once then walks to the actual clump before burial");
double toiletDue=movedTray.State.LitterClock.NextDue;
movedTray.MoveObject("litter",new Spot(500,500));Advance(movedTray,30);
Check(movedTray.State.Litter==20&&movedTray.State.LitterClock.NextDue==toiletDue,"moving litter during burial does not repeat toilet or resample its clock");
for(int level=0;level<5;level++)
{
 var cat=new PetEngine(new PetState{Litter=level*20},8);cat.Layout(1200,800);
 var deposit=cat.CareDestination(cat.LitterSpot,"toilet");cat.State.X=deposit.X;cat.State.Y=deposit.Y;
 cat.Demo("toilet");
 for(int tick=0;tick<500&&cat.Action!="bury";tick++)cat.Update(.1,12);
 Check(cat.Action=="bury"&&Math.Abs(cat.State.X+InteractionGeometry.BuryPawOffsetX-cat.LitterTarget.X)<.01
   &&Math.Abs(deposit.X+InteractionGeometry.ToiletDepositOffsetX-cat.LitterTarget.X)<.01,
   $"litter layer {level+1}: deposit and subsequent scratch target the same visible clump");
 Check(cat.State.X-40>=cat.LitterSpot.X-InteractionGeometry.LitterHalfWidth&&deposit.X+70<=cat.LitterSpot.X+InteractionGeometry.LitterHalfWidth,
   $"litter layer {level+1}: both care stances have room on the tray");
 cat.Refill("litter");Check(cat.Action!="bury","cleaning the target cancels burial immediately");
}
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
PetEngine RequestingCat(bool guiding=true)
{
 var pet=new PetEngine(new PetState{X=400,Y=400,Food=0,Water=75,Litter=20,TotalSeconds=301,FoodClock=new(){NextDue=99999,UnavailableSince=0},WaterClock=new(){NextDue=99999},LitterClock=new(){NextDue=99999}},17);
 pet.Layout(1200,800);pet.Update(.1,12);if(guiding)pet.ObservePointer(.1,true,pet.CatPlayCenter);return pet;
}
var debugCat=RequestingCat();debugCat.Demo("drink");Advance(debugCat,.5);
Check(debugCat.State.CareRequest is null&&!debugCat.State.Guiding&&debugCat.Action=="walk","demo drink overrides active guidance through subsequent frames");
var bedCat=RequestingCat();bedCat.BeginDrag();bedCat.Drop(true);Advance(bedCat,10);
Check(bedCat.Action=="sleep"&&bedCat.State.SleepingInNest&&new Spot(bedCat.State.X,bedCat.State.Y)==bedCat.Nest,"drop into nest stays asleep despite overdue shortage");
bedCat.Interact();Advance(bedCat,.1);
Check(bedCat.Action=="wake"&&!bedCat.State.Sleeping,"click immediately wakes cat even during protected nest sleep");
var overdueBed=RequestingCat();overdueBed.Refill("food");overdueBed.State.WaterClock.NextDue=1;
overdueBed.BeginDrag();overdueBed.Drop(true);Advance(overdueBed,14);
Check(overdueBed.Action=="sleep"&&overdueBed.State.Water==75,"overdue available care cannot bypass initial nest sleep grace");
Advance(overdueBed,2);Check(overdueBed.Action=="walk","needs can naturally wake nest cat after sleep grace");
var pendingBed=RequestingCat();pendingBed.BeginDrag();pendingBed.Drop(true);Advance(pendingBed,16);
Check(pendingBed.State.CareRequest=="food","shortage request resumes after protected sleep without losing its timer");
bool allDemosComplete=true;
foreach(string debugAction in new[]{"eat","drink","toilet","sleep"})
{
 var demoPet=RequestingCat();
 if(debugAction=="eat") {demoPet.Refill("food");demoPet.State.Water=0;demoPet.State.WaterClock.UnavailableSince=0;demoPet.Update(.1,12);}
 var wantedStock=debugAction=="eat"?demoPet.State.Food:demoPet.State.Water;
 demoPet.Demo(debugAction);bool reached=false;
 for(int i=0;i<1500;i++)
 {
  demoPet.Update(.1,12);
  if(demoPet.Action==debugAction){reached=true;break;}
 }
 allDemosComplete&=reached&&demoPet.State.CareRequest is null&&!demoPet.State.Guiding;
 if(debugAction is "eat" or "drink")
 {Advance(demoPet,4.8);allDemosComplete&=(debugAction=="eat"?demoPet.State.Food:demoPet.State.Water)==wantedStock-20;}
 else if(debugAction=="toilet") {Advance(demoPet,7.2);allDemosComplete&=demoPet.State.Litter==40;}
 else if(debugAction=="sleep") {Advance(demoPet,10);allDemosComplete&=demoPet.Action=="sleep"&&demoPet.State.SleepingInNest;}
 else {Advance(demoPet,2);allDemosComplete&=demoPet.Action=="cute";}
}
Check(allDemosComplete,"all five debug actions survive requests and finish their real care or sleep sequence");
var recalled=RequestingCat();double shortageStarted=recalled.State.FoodClock.UnavailableSince!.Value;recalled.Recall();var recalledAt=new Spot(recalled.State.X,recalled.State.Y);Advance(recalled,2);
Check(recalled.State.CareRequest is null&&new Spot(recalled.State.X,recalled.State.Y)==recalledAt,"recall clears guidance and remains at home instead of immediately walking away");
Advance(recalled,5);
Check(recalled.State.CareRequest=="food"&&recalled.State.FoodClock.UnavailableSince==shortageStarted,"manual command releases priority and retains unresolved shortage age");
var emptyDemo=RequestingCat();emptyDemo.State.X=emptyDemo.CareDestination(emptyDemo.FoodSpot,"eat").X;emptyDemo.State.Y=emptyDemo.FoodSpot.Y;emptyDemo.Demo("eat");Advance(emptyDemo,5);
Check(emptyDemo.State.Food==0&&emptyDemo.State.CareRequest=="food","empty-bowl demo never invents supplies and releases control to normal request");
var interruptDemo=RequestingCat();interruptDemo.State.X=interruptDemo.CareDestination(interruptDemo.WaterSpot,"drink").X;interruptDemo.State.Y=interruptDemo.WaterSpot.Y;interruptDemo.Demo("drink");Advance(interruptDemo,1.4);double waterAtPickup=interruptDemo.State.Water;
interruptDemo.BeginDrag();Advance(interruptDemo,5);interruptDemo.Drop(true);Advance(interruptDemo,1);
Check(interruptDemo.State.Water==waterAtPickup&&interruptDemo.Action=="sleep","drag interrupts debug consumption and dropping can enter protected sleep");
var placementCat=new PetEngine(new PetState{X=500,Y=700},1){Nest=new Spot(500,500)};
Check(placementCat.CanDropInNest(new Spot(500,460)),"placing held head inside visible nest counts even when feet extend below it");
Check(!placementCat.CanDropInNest(new Spot(500+InteractionGeometry.NestHalfWidth+20,460)),"releasing outside nest with distant feet remains an outside drop");
Check(placementCat.CanDropInNest(new Spot(418,406)),"nest highlight and release share inclusive visible-area boundary");
var primaryScreen=new PixelBounds(0,0,1920,1080);
var fullWindow=new FullscreenCandidate(primaryScreen,"GameWindow",true,false,false,false,false,false);
Check(FullscreenPolicy.Evaluate(fullWindow,primaryScreen).Hide,"borderless fullscreen on pet monitor hides scene");
Check(!FullscreenPolicy.Evaluate(fullWindow with {Maximized=true,HasCaption=true},primaryScreen).Hide,"ordinary maximized window stays visible even with auto-hidden taskbar");
Check(FullscreenPolicy.Evaluate(fullWindow with {Maximized=true},primaryScreen).Hide,"maximized borderless fullscreen still hides scene");
Check(!FullscreenPolicy.Evaluate(fullWindow with {Bounds=new(1920,0,3840,1080)},primaryScreen).Hide,"fullscreen on adjacent monitor does not hide primary pet");
Check(!FullscreenPolicy.Evaluate(fullWindow with {Bounds=new(-2560,-200,0,1240)},primaryScreen).Hide,"fullscreen on negative-coordinate monitor does not hide primary pet");
Check(FullscreenPolicy.Evaluate(fullWindow with {Bounds=new(-1920,0,1920,1080)},primaryScreen).Hide,"spanning fullscreen that covers pet monitor hides scene");
Check(!FullscreenPolicy.Evaluate(fullWindow with {Bounds=new(0,0,1920,1040)},primaryScreen).Hide,"ordinary working-area window does not count as fullscreen");
Check(!FullscreenPolicy.Evaluate(fullWindow with {Visible=false},primaryScreen).Hide&&
 !FullscreenPolicy.Evaluate(fullWindow with {Minimized=true},primaryScreen).Hide&&
 !FullscreenPolicy.Evaluate(fullWindow with {Cloaked=true},primaryScreen).Hide,"hidden minimized and virtual-desktop-cloaked windows do not suppress pet");
Check(!FullscreenPolicy.Evaluate(fullWindow with {OwnProcess=true},primaryScreen).Hide,"own scene and settings cannot trigger fullscreen hiding");
Check(new[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd"}.All(name=>!FullscreenPolicy.Evaluate(fullWindow with {ClassName=name},primaryScreen).Hide),"desktop and both taskbar classes are excluded");
Check(!FullscreenPolicy.Evaluate(null,primaryScreen).Hide&&!FullscreenPolicy.Evaluate(fullWindow with {Bounds=default},primaryScreen).Hide&&!FullscreenPolicy.Evaluate(fullWindow,default).Hide,"missing or invalid window and screen bounds restore scene");
Check(FullscreenPolicy.Evaluate(fullWindow with {Bounds=new(2,2,1918,1078)},primaryScreen).Hide&&!FullscreenPolicy.Evaluate(fullWindow with {Bounds=new(3,3,1917,1077)},primaryScreen).Hide,"fullscreen edge tolerance is limited to two physical pixels");
var fullscreenSequence=new[]{fullWindow,fullWindow with {Bounds=new(100,100,1000,800)},fullWindow,fullWindow with {Minimized=true}};
Check(fullscreenSequence.Select(window=>FullscreenPolicy.Evaluate(window,primaryScreen).Hide).SequenceEqual(new[]{true,false,true,false}),"fullscreen exit and minimization restore without sticky hidden state");
using var motionDoc=System.Text.Json.JsonDocument.Parse("""
{"clips":{"idle":{"fps":12,"loop":true},"walk":{"fps":24,"loop":true},"move-to-sit":{"fps":24,"loop":false},"sit-to-idle":{"fps":24,"loop":false},"sit-to-sleep":{"fps":16,"loop":false},"sleep":{"fps":12,"loop":true}}}
""");
var motion=new SpritePlayback();motion.Load(motionDoc.RootElement,_=>24);
Check(motion.Sample("idle",0)?.Clip=="idle"&&motion.Sample("sit",1.2)?.Index==14,"rest decisions do not restart a 24-frame blink every second");
Check(motion.Sample("walk",2)?.Clip=="walk"&&motion.Sample("guide-walk",2.5)?.Index==12,"movement aliases share a continuous gait");
Check(motion.Sample("sit",3)?.Clip=="move-to-sit","arrival plays movement-to-seat transition");
Check(motion.Sample("idle",4.1)?.Clip=="sit-to-idle"&&motion.Sample("sit",5.1)?.Clip=="idle","arrival completes seat-to-idle despite changing rest action revisions");
Check(motion.Sample("sleep",6)?.Clip=="sit-to-sleep"&&motion.Sample("sleep",7.6)?.Clip=="sleep","sleep entry completes once then uses shared sleeping loop");
Check(motion.Sample("wake",8)?.Index==23&&motion.Sample("wake",8.5)?.Index==15,"click wake immediately reverses sleep-entry pose");
Check(motion.Sample("drag",8.6) is null&&motion.Sample("walk",8.7)?.Clip=="walk","drag and renewed movement cancel pending visual transitions immediately");
motion.Reset();Check(motion.Sample("sleep",10)?.Clip=="sleep","saved sleeping cat opens directly in sleeping pose");
motion.Sample("walk",11);motion.Sample("sleep",12);
Check(motion.Sample("sleep",13.1)?.Clip=="sit-to-sleep"&&motion.Sample("sleep",14.6)?.Clip=="sleep","walk into bed chains sitting then sleep entry");
Check(motion.Sample("pet",15) is null,"unproduced interactions retain existing action rendering");
var noTransitions=new SpritePlayback();noTransitions.Load(motionDoc.RootElement,id=>id is "idle" or "walk" or "sleep"?24:0);
noTransitions.Sample("walk",0);Check(noTransitions.Sample("sit",1)?.Clip=="idle","missing transition cannot freeze the cat");
Check(motion.Sample("idle",double.NaN) is null,"invalid presentation clock does not select a corrupt frame");
var leftArrival=new PetEngine(new PetState{X=400,Y=400},1){FoodSpot=new Spot(300,400)};
leftArrival.Demo("eat");for(int i=0;i<60&&leftArrival.Action=="walk";i++)leftArrival.Update(.1,12);
Check(leftArrival.Action=="eat"&&leftArrival.FacingLeft,"leftward movement preserves facing on the exact arrival frame");
using var rightDoc=System.Text.Json.JsonDocument.Parse("""
{"clips":{"walk":{"fps":24,"loop":true},"walk-right":{"fps":18.46153846153846,"loop":true},"idle":{"fps":15,"loop":true},"move-to-sit":{"fps":24,"loop":false}}}
""");
var rightMotion=new SpritePlayback();rightMotion.Load(rightDoc.RootElement,id=>id=="walk-right"?30:24);
Check(rightMotion.Sample("walk",0,false)?.Clip=="walk-right"&&rightMotion.Sample("guide-walk",.5,false)?.Index==9,"right video is selected and remains continuous across movement aliases");
Check(rightMotion.Sample("walk",1.624,false)?.Index==29&&rightMotion.Sample("walk",1.626,false)?.Index==0,"right video preserves original 1.625 second cycle");
Check(rightMotion.Sample("walk",2,true)?.Clip=="walk"&&rightMotion.Sample("walk",3,false)?.Clip=="walk-right","direction changes keep old left gait and new right gait separate");
Check(rightMotion.Sample("sit",4)?.Clip=="move-to-sit","new right movement still transitions to sitting");
var videoEntries=Enumerable.Range(1,21).ToDictionary(n=>"video-"+n.ToString("00"),n=>new {fps=24,loop=n<=5||n is 14 or 20,mirrorWithFacing=false});
videoEntries["video-right"]=new {fps=24,loop=true,mirrorWithFacing=false};
using var videoDoc=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new {videoGraph=true,clips=videoEntries}));
var videos=new SpritePlayback();videos.Load(videoDoc.RootElement,id=>id=="video-right"?39:121);
Check(videos.Sample("idle",0)?.Clip=="video-01"&&videos.Sample("sit",5.05)?.Clip=="video-01","front idle keeps gentle blinking instead of continuously cycling large gestures");
Check(videos.Sample("walk",6,false)?.Clip=="video-06"&&videos.Sample("walk",11.05,false)?.Clip=="video-10"&&videos.Sample("walk",16.1,false)?.Clip=="video-right","right locomotion chains front rise and start before gait");
Check(videos.Sample("walk",17,true)?.Clip=="video-11"&&videos.Sample("walk",22.05,true)?.Clip=="video-15"&&videos.Sample("walk",27.1,true)?.Clip=="video-12"&&videos.Sample("walk",32.15,true)?.Clip=="video-14","right to left uses stop, real turn, start and independent left gait");
Check(videos.Sample("idle",33,true)?.Clip=="video-13"&&videos.Sample("idle",38.05,true)?.Clip=="video-09"&&videos.Sample("idle",43.1,true)?.Definition.MirrorWithFacing==false,"left arrival returns to front without swapping asymmetric eyes");
Check(videos.Sample("sleep",44)?.Clip=="video-17"&&videos.Sample("sleep",49.05)?.Clip=="video-19"&&videos.Sample("sleep",54.1)?.Clip=="video-20","sleep chains side sit and curl before breath loop");
Check(videos.Sample("wake",55)?.Clip=="video-21"&&videos.Sample("idle",60.05)?.Clip=="video-18","wake keeps wake-to-front transition after engine wake action ends");
Check(videos.Sample("drag",60.1) is null&&videos.Sample("eat",60.2) is null,"direct interactions immediately interrupt video transitions");
videos.Reset();Check(videos.Sample("sleep",0)?.Clip=="video-20","saved sleep opens directly in new sleep loop");
videos.Reset();Check(videos.Sample("walk",0,true)?.Clip=="video-08"&&videos.Sample("walk",5.05,true)?.Clip=="video-12"&&videos.Sample("walk",10.1,true)?.Clip=="video-14","left departure uses its own rise and start");
Check(videos.Sample("walk",11,false)?.Clip=="video-13"&&videos.Sample("walk",16.05,false)?.Clip=="video-16"&&videos.Sample("walk",21.1,false)?.Clip=="video-10","left to right uses true turn instead of image mirroring");
var heldMotion=new PetEngine(new PetState{X=400,Y=400},1){FoodSpot=new Spot(300,400),CanAdvanceMovement=(_,_,_)=>false};
heldMotion.Demo("eat");Advance(heldMotion,2);
Check(heldMotion.State.X==400&&heldMotion.Now>=1.9&&heldMotion.Action=="walk","visual preparation holds position while simulation clock continues");
heldMotion.BeginDrag();Check(heldMotion.Action=="drag","drag interrupts a position hold immediately");
var portions=new PetEngine(new PetState{Food=0,Water=0,Litter=100,X=400,Y=400,RestDuration=3600},1);
bool incremental=true;
for(int i=1;i<=5;i++){portions.Refill("food");portions.Refill("water");portions.Refill("litter");incremental&=portions.State.Food==i*20&&portions.State.Water==i*20&&portions.State.Litter==100-i*20;}
portions.Refill("food");portions.Refill("water");portions.Refill("litter");
Check(incremental&&portions.State.Food==100&&portions.State.Water==100&&portions.State.Litter==0,"five clicks add five food/water layers or clean five litter layers with bounded endpoints");
bool fiveMeals=true;
for(int i=1;i<=5;i++)
{
 portions.FoodSpot=new Spot(portions.State.X,portions.State.Y);portions.Demo("eat");Advance(portions,5);
 portions.WaterSpot=new Spot(portions.State.X,portions.State.Y);portions.Demo("drink");Advance(portions,5);
 fiveMeals&=portions.State.Food==100-i*20&&portions.State.Water==100-i*20;
}
Check(fiveMeals,"a full food and water bowl each supports exactly five completed visits");
Check(Enumerable.Range(0,6).Select(n=>PetEngine.SupplyLayers(n*20)).SequenceEqual(Enumerable.Range(0,6)),"visual stock has six discrete states from empty to five layers");
videos.Reset();videos.Sample("walk",0,false);
Check(videos.HorizontalVelocity("walk",2,false)>0,"rising footwork produces actual rightward velocity before walk loop");
videos.Reset();videos.Sample("walk",0,true);
Check(videos.HorizontalVelocity("walk",2,true)<0,"left rise footwork produces actual leftward velocity without mirroring");
Check(PetEngine.ToyRunSpeed==PetEngine.WalkSpeed&&PetEngine.ToyRunSpeed<58,"toy movement is capped to the same gait speed as walking");
var drifting=new PetEngine(new PetState{X=400,Y=400,RestDuration=600},1){FoodSpot=new Spot(600,400),VisualVelocity=(_,_,_)=>10};
drifting.Demo("eat");drifting.Update(.1,12);
Check(Math.Abs(drifting.State.X-401)<.001,"transition velocity moves world coordinates during rise rather than holding in place");
drifting.BeginDrag();double heldX=drifting.State.X;Advance(drifting,1);
Check(drifting.State.X==heldX,"drag cancels animation-driven world motion immediately");
string runtimeManifest=Path.Combine(Directory.GetCurrentDirectory(),"assets","pets","bluecat","manifest.json");
if(File.Exists(runtimeManifest))
{
 using var runtimeDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeManifest));
 var runtimeAnimations=runtimeDoc.RootElement.GetProperty("animations");var actualPlayback=new SpritePlayback();
 actualPlayback.Load(runtimeDoc.RootElement,id=>runtimeAnimations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
 var visited=new HashSet<string>();double playbackTime=0;bool framesValid=true;
 void PlayRoute(string action,double seconds,bool left=false)
 {
  for(int i=0;i<seconds*40;i++)
  {var frame=actualPlayback.Sample(action,playbackTime,left);if(frame is SpriteFrame f){visited.Add(f.Clip);framesValid&=f.Index>=0&&f.Index<f.Definition.Count&&!f.Definition.MirrorWithFacing;}playbackTime+=.025;}
 }
 PlayRoute("idle",26);PlayRoute("walk",6);PlayRoute("idle",8);PlayRoute("walk",6,true);PlayRoute("idle",8);
 PlayRoute("walk",6);PlayRoute("walk",12,true);PlayRoute("walk",12);PlayRoute("idle",8);
 PlayRoute("sleep",12);PlayRoute("wake",9);
 Check(Enumerable.Range(1,21).Where(n=>n==1||n>=6).All(n=>visited.Contains("video-"+n.ToString("00")))&&visited.Contains("video-right"),"rest and movement routes reach required clips without automatic large idle gestures");
 Check(framesValid,"all replacement route frames stay in bounds and preserve asymmetric eye direction");
}
var mattePixels=new byte[21*21*4];
for(int y=3;y<18;y++)for(int x=3;x<18;x++)
{int pixelOffset=(y*21+x)*4;byte color=(byte)(x==3||x==17||y==3||y==17?245:100);mattePixels[pixelOffset]=mattePixels[pixelOffset+1]=mattePixels[pixelOffset+2]=color;mattePixels[pixelOffset+3]=255;}
var cleanedMatte=AlphaMatte.Clean(mattePixels,21,21);
Check(cleanedMatte[(10*21+3)*4+3]<50&&cleanedMatte[(10*21+3)*4]<130,"white contamination is replaced by low-coverage fur color rather than a white fringe");
Check(cleanedMatte[(10*21+10)*4]==100&&cleanedMatte[(10*21+10)*4+3]==255,"matte repair preserves the solid interior color and opacity");
Check(cleanedMatte[3]==0&&mattePixels[(10*21+3)*4]==245,"matte correction preserves transparent background and never mutates source bytes");
Check(placementCat.CanDropInNest(new Spot(500,380)),"new tall plush bed shares its visible upper boundary with drop detection");
var diagonal=new byte[25*25*4];
for(int y=4;y<21;y++)for(int x=4;x<=y;x++){int q=(y*25+x)*4;diagonal[q]=diagonal[q+1]=diagonal[q+2]=100;diagonal[q+3]=255;}
var aa=AlphaMatte.Clean(diagonal,25,25);
Check(aa[(12*25+13)*4+3]>0&&aa[(12*25+12)*4+3]<255,"diagonal silhouette receives fractional coverage on both sides of hard pixel steps");
Check(aa[(12*25+13)*4]==100,"antialiasing uses premultiplied fur color without dark or white fringe");
var wakePosition=new PetEngine(new PetState());wakePosition.Layout(1200,800);wakePosition.Sleep();
var sleepingAnchor=wakePosition.VisualPosition;wakePosition.Interact();
Check(wakePosition.VisualPosition==sleepingAnchor,"click wake preserves the exact displayed nest anchor");
wakePosition.Wake();Check(wakePosition.VisualPosition==sleepingAnchor,"repeated wake never applies the nest offset twice");
wakePosition.Sleep();sleepingAnchor=wakePosition.VisualPosition;wakePosition.BeginDrag();
Check(wakePosition.VisualPosition==sleepingAnchor,"pickup from nest preserves displayed anchor before pointer movement");
var groundWake=new PetEngine(new PetState{Sleeping=true,SleepingInNest=false,X=400,Y=400});
var groundAnchor=groundWake.VisualPosition;groundWake.Interact();
Check(groundWake.VisualPosition==groundAnchor,"ground sleep wake does not apply a nest correction");
var floorCat=new PetEngine(new PetState{X=500,Y=200,RestDuration=600,NestPosition=new Spot(900,180),FoodPosition=new Spot(700,300),WaterPosition=new Spot(600,500),LitterPosition=new Spot(300,100)},2);
floorCat.Layout(1200,800);
Check(floorCat.State.Y==floorCat.GroundY&&new[]{floorCat.Nest,floorCat.FoodSpot,floorCat.WaterSpot,floorCat.LitterSpot}.All(v=>v.Y==floorCat.GroundY),"old saved vertical positions migrate to one floor while retaining horizontal placement");
Check(floorCat.State.X==500&&floorCat.FoodSpot.X==700,"floor migration preserves user horizontal positions");
foreach(var kind in new[]{"nest","food","water","litter"})floorCat.MoveObject(kind,new Spot(400,-999));
Check(new[]{floorCat.Nest,floorCat.FoodSpot,floorCat.WaterSpot,floorCat.LitterSpot}.All(v=>v.Y==floorCat.GroundY),"dragging every furniture item upward keeps its base on the floor");
floorCat.BeginDrag();floorCat.DragTo(new Spot(550,-999));floorCat.Drop(false);
Check(floorCat.State.X==550&&floorCat.State.Y==floorCat.GroundY,"cat drag changes only horizontal coordinate");
floorCat.Sleep();var floorWake=floorCat.VisualPosition;floorCat.Interact();Advance(floorCat,.1);
Check(floorCat.VisualPosition==floorWake&&floorCat.State.Y==floorCat.GroundY,"floor sleep and wake preserve world anchor without vertical relocation");
floorCat.Recall();floorCat.SetToy(true,new Spot(floorCat.State.X-200,floorCat.GroundY-180));Advance(floorCat,1);
Check(floorCat.State.Y==floorCat.GroundY,"elevated toy cannot pull cat off horizontal floor");
floorCat.SetToy(false,new Spot());floorCat.MoveObject("food",new Spot(700,100));floorCat.Demo("eat");
bool onFloor=true;for(int i=0;i<100;i++){floorCat.Update(.1,12);onFloor&=floorCat.State.Y==floorCat.GroundY;}
Check(onFloor&&floorCat.RequestSpot.Y==floorCat.GroundY&&floorCat.GuideDestination("food").Y==floorCat.GroundY,"care movement and guide destinations share floor throughout movement");
floorCat.Layout(900,600);Check(floorCat.State.Y==552&&floorCat.Nest.Y==552,"window resize recomputes floor for cat and furniture");
var isolatedMatte=(byte[])mattePixels.Clone();isolatedMatte[0]=isolatedMatte[1]=isolatedMatte[2]=250;isolatedMatte[3]=255;
var isolatedClean=AlphaMatte.Clean(isolatedMatte,21,21);
Check(isolatedClean[3]==0,"detached opaque white specks cannot bypass matte cleanup");
if(File.Exists(runtimeManifest))
{
 using var careDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeManifest));
 if(careDoc.RootElement.TryGetProperty("careVideoGraph",out var enabled)&&enabled.GetBoolean())
 {
  var animations=careDoc.RootElement.GetProperty("animations");var carePlayback=new SpritePlayback();
  carePlayback.Load(careDoc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
  var used=new HashSet<string>();double clock=0;bool validCare=true;
  foreach(var (action,left) in new[]{("eat",false),("drink",false),("toilet",false),("bury",false),("drag",false),("land",false),("toy-bat",false),("toy-bat",true),("pet",false),("rub",false),("paw",false),("roll",false),("guide-look",false),("guide-look",true),("request-food",false),("request-water",false),("request-litter",false),("guide-food",false),("guide-water",false),("guide-litter",false),("care-thanks",false)})
  {
   double span=carePlayback.ActionDuration(action,left)!.Value+.15;
   for(double time=0;time<span;time+=.04)
   {var f=carePlayback.Sample(action,clock,left)!.Value;used.Add(f.Clip);validCare&=f.Index>=0&&f.Index<f.Definition.Count&&!f.Definition.MirrorWithFacing;clock+=.04;}
  }
  Check(Enumerable.Range(22,30).All(n=>used.Contains("video-"+n)),"all 30 supplied care videos are reached by actual action mappings");
  Check(validCare,"care playback preserves independent directions and valid source frame indices");
  carePlayback.Reset();carePlayback.Sample("toy-bat",0,false);Check(carePlayback.Sample("idle",7,false)!.Value.Clip=="video-31","right toy interaction returns through the supplied paw-lowering clip");
  carePlayback.Reset();carePlayback.Sample("drag",0);Check(carePlayback.Sample("sleep",2)!.Value.Clip=="video-34","dropping into bed plays supplied landing before sleep route");
  carePlayback.Reset();carePlayback.Sample("eat",0);Check(carePlayback.Sample("drag",1)!.Value.Clip=="video-32","pickup interrupts care immediately without waiting for the full eating sequence");
  var careEngine=new PetEngine(new PetState{X=400,Y=400,Food=100,RestDuration=600},1){FoodSpot=new Spot(400,400),VisualActionDuration=carePlayback.ActionDuration,VisualConsumptionWindow=carePlayback.ConsumptionWindow};
  careEngine.Demo("eat");careEngine.Update(.1,12);
  var window=carePlayback.ConsumptionWindow("eat")!.Value;
  Check(window.Start>5&&window.Start<5.1&&window.Duration>5&&window.Duration<5.1,"preparation and actual intake both retain their original five-second duration");
  Advance(careEngine,window.Start-.2);Check(careEngine.State.Food==100,"lowering head does not consume inventory before the supplied eating loop");
  Advance(careEngine,window.Duration+.4);Check(careEngine.State.Food==80,"one supplied eating loop consumes exactly one of five layers");
  Advance(careEngine,6);Check(careEngine.State.Food==80,"raising head and completing care never consumes a second layer");
 }
}
PetEngine RestFixture()
{
 var pet=new PetEngine(new PetState{X=350,Y=752,RestDuration=3600,NestPosition=new Spot(1050,752),FoodPosition=new Spot(700,752),WaterPosition=new Spot(600,752),LitterPosition=new Spot(850,752)},3);
 pet.Layout(1200,800);return pet;
}
var placement=RestFixture();
Check(placement.IsClearRestSpot(350)&&!placement.IsClearRestSpot(600)&&!placement.IsClearRestSpot(1050),"rest footprint excludes bowls and bed rather than only checking cat center");
bool allClear=true;for(int x=65;x<1135;x+=7){var free=placement.NearestRestSpot(new(x,0));allClear&=placement.IsClearRestSpot(free.X)&&free.Y==752;}
Check(allClear,"nearest rest destination clears overlapping object intervals for every sampled target");
placement.State.X=700;placement.Update(.1,12);
Check(placement.Action=="walk"&&placement.State.X==700,"saved cat overlapping a bowl starts walking away without teleporting");
Advance(placement,12);Check(placement.IsClearRestSpot(placement.State.X)&&placement.Action is "sit" or "idle","overlapping saved placement reaches a free rest location");
placement=RestFixture();placement.MoveObject("food",new(placement.State.X,752));Advance(placement,8);
Check(placement.IsClearRestSpot(placement.State.X),"moving furniture over a resting cat makes the cat leave");
placement=RestFixture();placement.BeginDrag();placement.DragTo(placement.WaterSpot);placement.Drop(false);Advance(placement,12);
Check(placement.IsClearRestSpot(placement.State.X),"dropping on a bowl lands then walks to a clear rest point");
placement=RestFixture();placement.State.X=placement.CareDestination(placement.FoodSpot,"eat").X;placement.Demo("eat");placement.Update(.1,12);
Check(placement.Action=="eat"&&placement.State.X+InteractionGeometry.MouthOffsetX==placement.FoodSpot.X,"food interaction retains access to its occupied destination");
Advance(placement,18);Check(placement.IsClearRestSpot(placement.State.X)&&placement.State.Food==50,"finishing one meal leaves the bowl while consuming one layer");
placement=RestFixture();placement.State.X=placement.CareDestination(placement.WaterSpot,"drink").X;placement.Demo("drink");placement.Update(.1,12);
Check(placement.Action=="drink","water interaction remains reachable");
placement=RestFixture();placement.State.X=placement.CareDestination(placement.LitterSpot,"toilet").X;placement.Demo("toilet");placement.Update(.1,12);
Check(placement.Action=="toilet","toilet care remains exempt during actual use");
Advance(placement,25);Check(placement.IsClearRestSpot(placement.State.X),"completed toilet and bury sequence leaves the tray");
placement=RestFixture();placement.Sleep();Advance(placement,3);
Check(placement.State.SleepingInNest&&placement.State.X==placement.Nest.X,"intentional nest sleep is never evicted");
var nestAnchor=placement.VisualPosition;placement.Wake();Check(placement.VisualPosition==nestAnchor,"waking preserves nest anchor before walking out");
Advance(placement,20);Check(placement.IsClearRestSpot(placement.State.X),"awake cat eventually leaves the bed footprint");
placement=RestFixture();placement.State.X=600;placement.SetToy(true,new(600,687));Advance(placement,8);
Check(placement.IsClearRestSpot(placement.State.X),"toy over a bowl cannot keep cat stopped inside furniture");
placement=RestFixture();placement.Recall();Check(placement.IsClearRestSpot(placement.State.X),"recall chooses a clear point near the nest");
Check(placement.IsClearRestSpot(placement.RequestSpot.X)&&placement.IsClearRestSpot(placement.GuideDestination("food").X),"requests and guidance wait outside all furniture footprints");
var crowded=new PetEngine(new PetState{X=200,RestDuration=3600},3);crowded.Layout(420,400);
var fallback=crowded.NearestRestSpot(new(200,352));Check(double.IsFinite(fallback.X)&&fallback.X>=85&&fallback.X<=335&&crowded.NearestRestSpot(fallback)==fallback,"overfilled desktop uses stable bounded fallback instead of oscillating");
placement=RestFixture();placement.State.X=700;placement.Update(.1,12);Advance(placement,1);
placement.MoveObject("water",new(450,752));Advance(placement,14);
Check(placement.IsClearRestSpot(placement.State.X),"moving furniture across an escape destination retargets the active walk");
bool roamingClear=true;
for(int seed=0;seed<30;seed++)
{
 var wander=new PetEngine(new PetState{X=420,RestDuration=120,RestElapsed=121},seed);wander.Layout(1200,800);wander.Update(.1,12);Advance(wander,35);
 roamingClear&=wander.IsClearRestSpot(wander.State.X);
}
Check(roamingClear,"rest decisions remain outside furniture over multiple seeds");
// Long-horizon behavior checks: sleep is persistent, while real care still runs.
var quiet=new PetEngine(new PetState{X=250,Y=752,RestDuration=120},9);quiet.Layout(1200,800);
Advance(quiet,55);long quietRevision=quiet.ActionRevision;
Check(quietRevision<=1,"quiet idle does not reroll its behavior every second");
Advance(quiet,6);double quietX=quiet.State.X;Advance(quiet,3700);
Check(quiet.Action=="sleep"&&quiet.State.X==quietX,"sleep persists across rest deadlines without forced wake or roaming");
for(int seed=0;seed<40;seed++)
{
 var wakeCat=new PetEngine(new PetState{X=250,Y=752,Sleeping=true,SleepingInNest=false},seed);wakeCat.Layout(1200,800);
 wakeCat.Interact();Advance(wakeCat,20);
 if(wakeCat.State.X!=250||wakeCat.State.Sleeping)throw new Exception("click wake unexpectedly relocates or immediately sleeps");
}
Check(true,"forty wake seeds stay put and allow interaction instead of random relocation");
using(var calmDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeManifest)))
{
 var animations=calmDoc.RootElement.GetProperty("animations");
 foreach(int seed in new[]{7,21,43})
 {
  var pb=new SpritePlayback();pb.Load(calmDoc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
  var cat=new PetEngine(new PetState{X=350,Y=752,Food=100,Water=100,Litter=0},seed);
  cat.Layout(1200,800);cat.VisualActionDuration=pb.ActionDuration;cat.VisualConsumptionWindow=pb.ConsumptionWindow;
  cat.VisualVelocity=pb.HorizontalVelocity;cat.VisualCareReady=pb.PrepareCare;
  int sleeping=0,moving=0,drinks=0,meals=0;string previous="";
  for(int step=0;step<36000;step++)
  {
   cat.AdvanceNeeds(.1);cat.Update(.1,12);pb.Sample(cat.AligningForCare?"care-ready":cat.Action,cat.Now,cat.FacingLeft);
   if(cat.Action=="sleep")sleeping++;if(cat.Action=="walk")moving++;
   if(cat.Action!=previous){if(cat.Action=="drink")drinks++;if(cat.Action=="eat")meals++;}previous=cat.Action;
   // Owner keeps supplies available; replenishment does not interact with the cat.
   if(cat.State.Water<20)cat.Refill("water");if(cat.State.Food<20)cat.Refill("food");
  }
  Console.WriteLine($"CALM seed={seed} sleep={sleeping/360d:F1}% move={moving/360d:F1}% drinks={drinks} meals={meals}");
  Check(sleeping>36000*.80&&moving<36000*.10&&drinks>0&&meals>0,"one-hour native-animation simulation mostly sleeps and still completes scheduled care");
 }
}
// Exercise actual native-video arrival routing, including a left-facing approach.
using(var alignmentDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeManifest)))
{
 var animations=alignmentDoc.RootElement.GetProperty("animations");
 foreach(bool fromRight in new[]{false,true})
 {
  var pb=new SpritePlayback();pb.Load(alignmentDoc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
  var cat=RestFixture();cat.State.X=fromRight?850:350;
  cat.VisualVelocity=pb.HorizontalVelocity;cat.VisualCareReady=pb.PrepareCare;
  cat.VisualActionDuration=pb.ActionDuration;cat.VisualConsumptionWindow=pb.ConsumptionWindow;
  cat.Demo("eat");var route=new HashSet<string>();bool reached=false;
  for(int i=0;i<800;i++)
  {
   cat.Update(.05,12);var frame=pb.Sample(cat.AligningForCare?"care-ready":cat.Action,cat.Now,cat.FacingLeft);
   if(frame is {} f)route.Add(f.Clip);
   if(cat.Action=="eat"){reached=true;break;}
  }
  Check(reached&&Math.Abs(cat.State.X+InteractionGeometry.MouthOffsetX-cat.FoodSpot.X)<.01,"care reaches a mouth-aligned location from either approach direction");
  Check(route.Contains(fromRight?"video-16":"video-11"),"care completes real stop/turn footage before lowering the head");
  Check(cat.State.Food==70,"arrival and turn do not consume food");
  cat.BeginDrag();cat.Update(.1,12);Check(!cat.AligningForCare&&cat.Action=="drag","pickup interrupts pending care alignment");
 }
}
var edgeCare=RestFixture();edgeCare.MoveObject("food",new(-100,0));
Check(edgeCare.CareDestination(edgeCare.FoodSpot,"eat").X>=65,"left-edge bowl leaves enough room for the supplied right-facing mouth pose");
edgeCare.MoveObject("litter",new(-100,0));
Check(edgeCare.LitterSpot.X>=InteractionGeometry.LitterHalfWidth,"enlarged tray stays within the screen");
Check(InteractionGeometry.SurfaceLift("video-20",.5,true,"sleep")==InteractionGeometry.NestCushionLift&&InteractionGeometry.SurfaceLift("video-19",1,true,"sleep")==InteractionGeometry.NestCushionLift,"sleep entry and loop share the cushion surface");
Check(InteractionGeometry.SurfaceLift("video-21",1,true,"wake")==InteractionGeometry.SurfaceLift("video-18",0,true,"wake")&&InteractionGeometry.SurfaceLift("video-18",1,true,"wake")==InteractionGeometry.NestCushionLift,"wake remains fully supported until the cat actually walks out of the bed");
Check(InteractionGeometry.SurfaceLift("video-33",.5,false,"drag")==24&&InteractionGeometry.SurfaceLift("video-34",1,false,"land")==0,"pickup clears floor and landing returns to it");
Check(InteractionGeometry.SurfaceLift("video-27",.5,false,"toilet")==50&&InteractionGeometry.SurfaceLift("video-30",.5,false,"bury")==50,"toilet and burial share the enlarged tray surface");
var calibrated=new SpriteClip(121,24,false,288,288,.5,.921875,false,.99,1.02);
Check(Math.Abs(calibrated.AtFrame(0).Width-285.12)<.001&&Math.Abs(calibrated.AtFrame(120).Width-293.76)<.001&&calibrated.AtFrame(60).AnchorY==calibrated.AnchorY,"pose scaling uses smooth endpoint metadata around a fixed root");
using(var phaseDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeManifest)))
{
 var a=phaseDoc.RootElement.GetProperty("animations");var phases=phaseDoc.RootElement.GetProperty("clips").GetProperty("video-20").GetProperty("phaseRegistration");
 bool phasesCorrect=true,repeatedStable=true,interruptible=true;
 for(int i=0;i<phases.GetArrayLength();i++)
 {
  var pb=new SpritePlayback();pb.Load(phaseDoc.RootElement,id=>a.TryGetProperty(id,out var frames)?frames.GetArrayLength():0);
  pb.Sample("sleep",0);double now=i/24d+.000001;pb.Sample("sleep",now);
  var wake=pb.Sample("wake",now+.001)!.Value;var again=pb.Sample("wake",now+.001)!.Value;
  phasesCorrect&=wake.Clip=="video-21"&&Math.Abs(wake.Definition.Width-288/phases[i][0].GetDouble())<.1;
  repeatedStable&=wake==again;
  interruptible&=pb.Sample("drag",now+.002)!.Value.Clip=="video-32";
 }
 Check(phasesCorrect,"every sleeping breath phase preserves body scale on immediate wake");
 Check(repeatedStable,"sampling velocity and rendering at the same time never reapplies wake correction");
 Check(interruptible,"pickup interrupts wake continuation at every breath phase");
 var pb2=new SpritePlayback();pb2.Load(phaseDoc.RootElement,id=>a.TryGetProperty(id,out var frames)?frames.GetArrayLength():0);
 pb2.Sample("sleep",0);pb2.Sample("sleep",2.5);pb2.Sample("wake",2.501);
 var settled=pb2.Sample("wake",3.101)!.Value;
 var reference=new SpritePlayback();reference.Load(phaseDoc.RootElement,id=>a.TryGetProperty(id,out var frames)?frames.GetArrayLength():0);
 reference.Sample("sleep",0);reference.Sample("wake",.001);var expected=reference.Sample("wake",.601)!.Value;
 Check(Math.Abs(settled.Definition.Width-expected.Definition.Width)<.001,"wake continuation ends after half a second without accumulating scale drift");
}
var registered=new SpriteClip(121,24,false,288,288,.5,.921875,false,1,1,2,-3,2,-3).AtFrame(60);
Check(Math.Abs((.5-registered.AnchorX)*registered.Width-2)<.001&&Math.Abs((.921875-registered.AnchorY)*registered.Height+3)<.001,"feature registration moves the shared root without stretching the pose");
Check(InteractionGeometry.NestSupport(660,680)==InteractionGeometry.NestCushionLift&&InteractionGeometry.NestSupport(610,680)==InteractionGeometry.NestCushionLift&&InteractionGeometry.NestSupport(460,680)==0,"bed exit height follows horizontal progress rather than the wake clip timer");
Check(InteractionGeometry.SurfaceLift("video-07",.5,true,"sleep")==InteractionGeometry.NestCushionLift&&InteractionGeometry.SurfaceLift("video-17",0,true,"sleep")==InteractionGeometry.NestCushionLift,"early sleep entry poses cannot sink below the cushion");
Check(InteractionGeometry.SurfaceLift("video-32",0,true,"drag")==InteractionGeometry.NestCushionLift+InteractionGeometry.PickupLift,"lifting from the bed starts above its cushion instead of dropping toward the floor");
foreach(string destination in new[]{"sleep","toilet"})
{
 var cat=new PetEngine(new PetState{Litter=0},5);cat.Layout(1400,800);
 cat.MoveObject(destination=="sleep"?"nest":"litter",new(750,cat.GroundY));
 cat.State.X=400;cat.Demo(destination);
 double prior=0;bool rising=false,continuous=true;
 for(double x=450;x<730;x+=1)
 {cat.State.X=x;double height=cat.Support.Update(cat);rising|=height>0;continuous&=height>=prior&&height-prior<2;prior=height;}
 Check(rising&&continuous&&cat.Action=="walk",$"{destination}: support rises continuously before arrival instead of at the action switch");
 var endpoint=cat.CareDestination(destination=="sleep"?cat.Nest:cat.LitterSpot,destination);cat.State.X=endpoint.X;
 double before=cat.Support.Update(cat);cat.Update(.1,12);double after=cat.Support.Update(cat);
 Check(Math.Abs(before-after)<.01,$"{destination}: entering the action preserves its already-reached support height");
 Check(destination!="sleep"||Math.Abs(cat.VisualPosition.X-endpoint.X)<.01,"walking into the bed preserves the same horizontal root as sleeping");
 cat.Interact();cat.Support.Update(cat);
 Check(cat.Support.Height>0,"interrupting a supported action does not drop the cat to the floor");
 cat.State.X=350;Check(cat.Support.Update(cat)==0&&cat.Support.Kind is null,"leaving a prop clears its support without affecting ordinary floor travel");
}
var passing=new PetEngine(new PetState{X=500},2);passing.Layout(1400,800);passing.State.X=passing.Nest.X;
Check(passing.Support.Update(passing)==0,"passing an unused prop does not attach a floor cat to its raised surface");
Check(home.CanDropInNest(new Spot(home.Nest.X+InteractionGeometry.NestHalfWidth-1,home.Nest.Y-InteractionGeometry.NestHeight+1)),"expanded bed artwork shares its actual drop bounds");
Check(!InteractionGeometry.InsideNestSeat(500,641)&&InteractionGeometry.NestSupport(500,641)>0&&InteractionGeometry.InsideNestSeat(621,641),"climbing across the arm stays in front until the body enters the seat");
using(var groundedDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeManifest)))
{
 var entries=groundedDoc.RootElement.GetProperty("clips");int measured=0;bool correct=true;
 foreach(var item in entries.EnumerateObject())
 {
  if(!item.Value.TryGetProperty("groundContacts",out var contacts))continue;
  var profile=SpritePlayback.ReadGroundContacts(item.Value,contacts.GetArrayLength())!;
  var clip=new SpriteClip(profile.Length,24,true,288,288,.5,.921875,false,GroundContacts:profile);
  for(int i=0;i<profile.Length;i++){var frame=clip.AtFrame(i);correct&=Math.Abs((profile[i]-frame.AnchorY)*frame.Height)<.001&&frame.Width==288;measured++;}
 }
 Check(correct&&measured==3463,"all 3463 grounded poses place their measured contact on the support plane without rescaling");
 Check(!entries.GetProperty("video-32").TryGetProperty("groundContacts",out _)&&!entries.GetProperty("video-33").TryGetProperty("groundContacts",out _),"pickup and suspended motion retain their authored vertical trajectory");
}
using(var guideDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeManifest)))
{
 var animations=guideDoc.RootElement.GetProperty("animations");var entries=guideDoc.RootElement.GetProperty("clips");
 var pb=new SpritePlayback();pb.Load(guideDoc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);
 Check(entries.EnumerateObject().All(c=>!c.Value.TryGetProperty("playbackRate",out _)),"all global playback overrides are removed, including movement, sleep and care transitions");
 pb.Sample("bury",0);var raised=pb.Sample("bury",.5)!.Value;pb.Reset();var toyRaised=pb.Sample("toy-bat",0)!.Value;
 Check(raised.Clip=="video-29"&&raised.Definition.Fps==72&&toyRaised.Clip=="video-29"&&toyRaised.Definition.Fps==24,"only burial uses faster paw raising; the shared toy gesture keeps its original speed");
 pb.Reset();pb.Sample("bury",0);var lower=pb.Sample("bury",7)!.Value;
 Check(lower.Clip=="video-31"&&lower.Definition.Duration>5&&lower.Definition.Duration<5.1,"burial paw lowering also keeps its original five seconds");
 foreach(bool follow in new[]{false,true})
 {
  pb.Reset();var cat=new PetEngine(new PetState{X=1200,Food=100,Water=0,Litter=0,NestPosition=new(2237,1242),FoodPosition=new(1989,1242),WaterPosition=new(2064,1242),LitterPosition=new(412,1242)},5);
  cat.Layout(2400,1290);cat.State.X=cat.RequestSpot.X;cat.AdvanceNeeds(301);cat.Update(.02,12);
  cat.VisualActionDuration=pb.ActionDuration;cat.VisualVelocity=pb.HorizontalVelocity;cat.VisualStandReady=pb.PrepareStand;cat.VisualCareReady=pb.PrepareCare;cat.VisualTravel=pb.TravelTo;
  Spot pointer=new(cat.State.X-30,cat.State.Y-65);string previous=cat.Action;double lookStart=0,lastX=cat.State.X;int waits=0;bool faces=true,still=true,whole=true,monotone=true;var route=new List<string>();
  for(int tick=0;tick<8000;tick++)
  {
   if(follow)pointer=new(cat.State.X-30,cat.State.Y-65);
   cat.ObservePointer(.02,true,pointer);cat.Update(.02,12);
   var frame=pb.Sample(cat.Action,cat.Now,cat.FacingLeft)!.Value;
   if(route.Count==0||route[^1]!=frame.Clip)route.Add(frame.Clip);
   if(cat.Action=="guide-look")
   {faces&=!cat.FacingLeft&&frame.Clip=="video-43";if(previous!="guide-look"){lookStart=cat.Now;waits++;}else still&=Math.Abs(cat.State.X-lastX)<.001;}
   if(previous=="guide-look"&&cat.Action!="guide-look")whole&=cat.Now-lookStart>=5;
   monotone&=cat.State.X>=lastX-.001;lastX=cat.State.X;previous=cat.Action;
  }
  Check(waits>0&&faces&&still&&whole&&monotone,$"native guide with {(follow?"following":"stationary")} pointer waits five seconds without flipping or walking backwards");
  Check(route.Zip(route.Skip(1)).Where(p=>p.Second=="video-43").All(p=>p.First=="video-11"),"each guide look is preceded by the actual right stopping clip");
  Check(follow?cat.Action=="guide-water":cat.Action=="guide-look","following reaches the item; stationary pointer settles into a stable wait");
 }
 // Opposite destination while waiting must traverse the authored turn.
 pb.Reset();pb.Sample("guide-look",0,false);var reverseRoute=new List<string>();
 for(double reverseTime=5.1;reverseTime<20;reverseTime+=.02){var f=pb.Sample("guide-walk",reverseTime,true)!.Value;if(reverseRoute.Count==0||reverseRoute[^1]!=f.Clip)reverseRoute.Add(f.Clip);}
 Check(reverseRoute.Take(3).SequenceEqual(new[]{"video-15","video-12","video-14"}),"reversing after a guide look plays the real turn, then starts the opposite gait");
}
var rubbing=new PetEngine(new PetState{X=500,Y=400},5);rubbing.ObservePointer(5.1,true,new(510,330));
for(int i=0;i<35;i++){rubbing.ObservePointer(.1,true,new(500+(i%2==0?1:-1),330));rubbing.Update(.1,12);}
Check(rubbing.State.X==500&&rubbing.Action=="rub","ordinary hover rubbing stays put even when the pointer crosses the cat center");

using(var travelDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeManifest)))
{
 var animations=travelDoc.RootElement.GetProperty("animations");
 SpritePlayback Player(){var playback=new SpritePlayback();playback.Load(travelDoc.RootElement,id=>animations.TryGetProperty(id,out var a)?a.GetArrayLength():0);return playback;}
 foreach(double delta in new[]{1d,8,35,95,500,-1,-8,-35,-95,-500})
 {
  var playback=Player();double x=800,end=x+delta,start=x,doneAt=0;bool bounded=true;var seen=new Dictionary<string,(double First,double Last,int Max)>();
  for(double at=0;at<90;at+=.01)
  {
   var move=playback.TravelTo("walk",x,end,at,delta<0)!.Value;x=move.X;
   var frame=playback.Sample("walk",at,delta<0)!.Value;
   bounded&=x>=Math.Min(start,end)-.001&&x<=Math.Max(start,end)+.001;
   if(!seen.TryGetValue(frame.Clip,out var span))span=(at,at,0);
   seen[frame.Clip]=(span.First,at,Math.Max(span.Max,frame.Index));
   if(move.Complete){doneAt=at;break;}
  }
  string begin=delta<0?"video-08":"video-06",step=delta<0?"video-12":"video-10",stop=delta<0?"video-13":"video-11";
  Check(doneAt>0&&bounded&&Math.Abs(x-end)<.001,$"{delta}-unit leg reaches exactly its target without crossing it");
  Check(new[]{begin,step,stop}.All(id=>seen.TryGetValue(id,out var span)&&span.Max>=animations.GetProperty(id).GetArrayLength()-2),$"{delta}-unit leg retains full stand/start/stop footage");
 }

 foreach(double distance in new[]{0d,1,8,35,-1,-8,-35})
 {
  var player=Player();var approachCat=new PetEngine(new PetState{X=600,Food=80},5);approachCat.Layout(1800,800);approachCat.MoveObject("food",new(694+distance,approachCat.GroundY));
  approachCat.VisualTravel=player.TravelTo;approachCat.VisualCareReady=player.PrepareCare;approachCat.VisualActionDuration=player.ActionDuration;approachCat.VisualConsumptionWindow=player.ConsumptionWindow;
  approachCat.Demo("eat");double goal=approachCat.CareDestination(approachCat.FoodSpot,"eat").X;bool eating=false,earlyBites=false,endpoint=false;
  for(int tick=0;tick<9000;tick++)
  {
   approachCat.Update(.01,12);var frame=player.Sample(approachCat.AligningForCare?"care-ready":approachCat.Action,approachCat.Now,approachCat.FacingLeft)!.Value;
   if(approachCat.Action=="eat"){eating=true;endpoint=Math.Abs(approachCat.State.X-goal)<.001;if(approachCat.ActionTime<5)earlyBites|=approachCat.State.Food!=80;if(approachCat.ActionTime>6)break;}
  }
  Check(eating&&endpoint&&!earlyBites,$"native {distance}-unit food approach aligns before care and keeps its five-second preparation");
 }
 var changing=Player();double movingX=800;
 for(double at=0;at<.8;at+=.01)movingX=changing.TravelTo("walk",movingX,805,at,false)!.Value.X;
 double previous=movingX;var changed=changing.TravelTo("walk",movingX,760,.8,true)!.Value;
 Check(Math.Abs(changed.X-previous)<.01,"retargeting a short leg preserves the current position");
 bool changedDirectionSafely=true,sawTurn=false,sawLeftStep=false;
 for(double at=.81;at<12;at+=.01)
 {
  var move=changing.TravelTo("walk",movingX,760,at,true)!.Value;movingX=move.X;
  var frame=changing.Sample("walk",at,true)!.Value;
  if(frame.Clip is "video-06" or "video-15")changedDirectionSafely&=Math.Abs(movingX-previous)<.001;
  sawTurn|=frame.Clip=="video-15";sawLeftStep|=frame.Clip=="video-12";
 }
 Check(changedDirectionSafely&&sawTurn&&sawLeftStep,"retargeting behind the cat completes its current pose and real turn before opposite displacement");
 Check(changing.Sample("drag",12.01)!.Value.Clip=="video-32","pickup immediately interrupts a planned movement leg");
 var burialPlayer=Player();var cat=new PetEngine(new PetState{X=500,Litter=0},5);cat.Layout(1800,800);cat.MoveObject("litter",new(700,cat.GroundY));
 cat.VisualTravel=burialPlayer.TravelTo;cat.VisualCareReady=burialPlayer.PrepareCare;cat.VisualActionDuration=burialPlayer.ActionDuration;cat.VisualBurialWindow=burialPlayer.BurialWindow;
 cat.Demo("toilet");bool fresh=false,partial=false,finished=false;double partialCover=0;
 for(int i=0;i<14000;i++)
 {
  cat.Update(.01,12);burialPlayer.Sample(cat.AligningForCare?"care-ready":cat.Action,cat.Now,cat.FacingLeft);
  fresh|=cat.State.Litter==20&&cat.State.LitterCover[0]==0;
  partial|=cat.State.LitterCover[0]>.15&&cat.State.LitterCover[0]<.85;
  if(cat.State.LitterCover[0]>.45&&partialCover==0)
  {
   partialCover=cat.State.LitterCover[0];
   var roundtrip=System.Text.Json.JsonSerializer.Deserialize<PetState>(System.Text.Json.JsonSerializer.Serialize(cat.State))!;
   var restored=new PetEngine(roundtrip,5);Check(Math.Abs(restored.State.LitterCover[0]-partialCover)<.00001,"partially buried waste survives save/load");
  }
  if(cat.State.LitterCover[0]==1&&cat.Action!="bury"){finished=true;break;}
 }
 Check(fresh&&partial&&finished&&cat.State.Litter==20,"toilet leaves exposed waste; digging progressively covers it without cleaning inventory");
 cat.Refill("litter");Check(cat.State.Litter==0&&cat.State.LitterCover.All(c=>c==0),"cleaning removes the clump and its burial progress");
 var interrupted=new PetEngine(new PetState{Litter=40,LitterCover=new[]{1d,.4,0,0,0}},3);interrupted.BeginDrag();interrupted.Update(.1,12);
 Check(interrupted.State.LitterCover[1]==.4,"interrupting care never marks unfinished burial complete");
 var legacy=new PetEngine(new PetState{Litter=40,LitterCover=null!},3);Check(legacy.State.LitterCover.Length==5&&legacy.State.LitterCover.All(c=>c==0),"legacy saves migrate to visible unburied clumps without inventing completed burial");
}
Check(InteractionGeometry.BowlY(500,500)==500&&InteractionGeometry.BowlY(500,456)>456,"lower bowl geometry keeps its base grounded and lowers its rim");

Console.WriteLine($"{checks} checks passed.");
