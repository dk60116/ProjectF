using System;
using System.Collections.Generic;
public readonly record struct Vector2Int(int x, int y);
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Min(float a,float b)=>Math.Min(a,b);
 public static float Min(float a,float b,float c)=>Math.Min(a,Math.Min(b,c));
}
public static class MapObjectTickManager { public static long CurrentSimulationTick=10; public const float FixedSimulationDeltaSeconds=.02f; }
public static class MapObjectTickProfiler {
 public sealed class Sample : IDisposable { public void Dispose() {} }
 public static Sample SampleNamed(string a,string b,string c)=>new();
}
public static class DeterministicSimulationUnits {
 public static long FromFloat(float value)=>(long)Math.Round(value*1000000d);
 public static float ToFloat(long value)=>value/1000000f;
}
public partial class InstallationObject {
 public long StoredFluidUnits=>DeterministicSimulationUnits.FromFloat(StoredFluidLiters);
 public float Temperature=20;
 public float GetStoredFluidTemperatureCelsius(int id)=>Temperature;
 public void SetStoredFluidUnits(int id,long units,float temperature) {StoredFluidLiters=DeterministicSimulationUnits.ToFloat(units); StoredFluidItemId=units>0?id:-1; Temperature=temperature;}

 public sealed class State { public bool activeInHierarchy=true; }
 public State gameObject=new();
 public float StoredFluidLiters;
 public int StoredFluidItemId=1;
 public float Capacity=100;
 public bool AcceptIncoming=true;
 public float ReceiverLimit=float.MaxValue;
 public int ConsumeCalls;
 public bool CanProvideFluidItem(int id,float liters=0)=>StoredFluidItemId==id && StoredFluidLiters>0 && StoredFluidLiters>=liters;
 public bool TryConsumeFluidLiters(int id,float request,out float consumed) {
  consumed=0; if(!CanProvideFluidItem(id)) return false;
  consumed=Math.Min(request,StoredFluidLiters); StoredFluidLiters-=consumed; ConsumeCalls++;
  if(StoredFluidLiters==0) StoredFluidItemId=-1;
  return consumed>0;
 }
 public bool TryAddFluidLiters(int id,float request,float temperature,out float accepted) {
  accepted=AcceptIncoming ? Math.Min(Math.Min(request,ReceiverLimit),Capacity-StoredFluidLiters) : 0;
  StoredFluidLiters+=accepted; if(accepted>0) StoredFluidItemId=id; return accepted>0;
 }
 protected static float CalculateFluidPressureRetention(int distance)=>Math.Max(0,1-distance*.01f);
}
public partial class InputOutputModule : InstallationObject {
 private readonly FluidTransferPreview fluidTransferPreviewScratch=new();
 public float ManagedUpdateTickIntervalSeconds=>.1f;
 private readonly List<FluidOutputConnection> cachedFluidOutputConnections=new();
 private sealed class FluidPortConnectionCache { public readonly List<FluidOutputConnection> Connections=new(); }
 private readonly Dictionary<Vector2Int,FluidPortConnectionCache> fluidInputPortConnectionCaches=new();
 private readonly Dictionary<Vector2Int,FluidPortConnectionCache> fluidOutputPortConnectionCaches=new();
 private static bool IsFluidItemId(int id)=>id>=0;
 private bool EnsureFluidOutputStorageCache(IReadOnlyList<Vector2Int> seeds)=>true;
 private bool CanUseFluidOutputConnectionWithAnySpace(FluidOutputConnection c,int id)=>c.Storage!=null && c.Storage.StoredFluidLiters<c.Storage.Capacity;
 private float GetFluidOutputConnectionAvailableLiters(FluidOutputConnection c,int id)=>c.Storage.Capacity-c.Storage.StoredFluidLiters;
 private float GetFluidOutputConnectionFillRatio(FluidOutputConnection c,int id)=>c.Storage.StoredFluidLiters/c.Storage.Capacity;
 private FluidPortConnectionCache GetFluidPortConnectionCache(Dictionary<Vector2Int,FluidPortConnectionCache> caches,Vector2Int coordinate,bool input) {
  if(!caches.TryGetValue(coordinate,out var cache)) caches[coordinate]=cache=new(); return cache;
 }
 public bool UsesDedicatedFluidStorageAtRuntimeCoordinate(Vector2Int c)=>false;
 public bool TryAddDedicatedFluidAtRuntimeCoordinate(Vector2Int c,int id,float requested,float temperature,out float accepted)=>TryAddFluidLiters(id,requested,temperature,out accepted);
 public void AddRoute(InstallationObject storage,Pump pump,bool input=false,int port=0) {
  var c=new FluidOutputConnection(storage,new(port,0),0,pump); cachedFluidOutputConnections.Add(c);
  GetFluidPortConnectionCache(input?fluidInputPortConnectionCaches:fluidOutputPortConnectionCaches,new(port,0),input).Connections.Add(c);
 }
 public float Transfer(InstallationObject source,float requested) { TryTransferFluidFromStorageToConnectedStorage(source,1,requested,20,new[]{new Vector2Int(0,0)},out float accepted); return accepted; }
 public float PreviewAcrossPorts(float requested) {
  var preview=new FluidTransferPreview();
  TryGetConnectedFluidInputAvailableLitersAtCoordinate(new(0,0),1,requested,out float first,preview);
  TryGetFluidOutputAvailableLitersAtCoordinate(new(0,0),1,requested,out float second,preview);
  return first+second;
 }
 public float Preview(bool input,float requested,int port=0) {
  float available;
  if(input) TryGetConnectedFluidInputAvailableLitersAtCoordinate(new(port,0),1,requested,out available);
  else TryGetFluidOutputAvailableLitersAtCoordinate(new(port,0),1,requested,out available);
  return available;
 }
}
public partial class Pump : InputOutputModule {
 public float PressureLitersPerSecond=5;
 private long pressureBudgetTick=-1;
 private double pressureBudgetLiters;
}
public static class Checks {
 static int passed,failed;
 static void Eq(float actual,float expected,string name) { if(Math.Abs(actual-expected)<.0001f) passed++; else {failed++; Console.WriteLine($"FAIL {name}: expected {expected}, got {actual}");} }
 public static int Main() {
  var pump=new Pump(); var source=new InstallationObject{StoredFluidLiters=10,AcceptIncoming=false}; var destination=new InstallationObject();
  pump.AddRoute(destination,pump);
  Eq(pump.Transfer(source,5),.5f,"partial pump allowance is delivered");
  Eq(source.StoredFluidLiters+destination.StoredFluidLiters,10,"unloading-only tank conserves rejected volume");
  int calls=source.ConsumeCalls;
  Eq(pump.Transfer(source,5),0,"exhausted pump transfers nothing");
  Eq(source.StoredFluidLiters+destination.StoredFluidLiters,10,"exhausted pump cannot delete fluid");
  Eq(source.ConsumeCalls,calls,"zero allowance does not debit source");
  var blockedPump=new Pump(); var blockedSource=new InstallationObject{StoredFluidLiters=.25f,AcceptIncoming=false};
  blockedPump.AddRoute(new InstallationObject{AcceptIncoming=false},blockedPump);
  blockedPump.Transfer(blockedSource,5);
  Eq(blockedSource.StoredFluidLiters,.25f,"receiver rejection restores last fluid even when refill is forbidden");
  var partialPump=new Pump(); var partialSource=new InstallationObject{StoredFluidLiters=.5f,AcceptIncoming=false};
  var partialTarget=new InstallationObject{ReceiverLimit=.1f}; partialPump.AddRoute(partialTarget,partialPump);
  partialPump.Transfer(partialSource,5);
  Eq(partialSource.StoredFluidLiters+partialTarget.StoredFluidLiters,.5f,"partial receiver acceptance preserves total volume");
  var sharedPump=new Pump(); var machine=new InputOutputModule();
  machine.AddRoute(new InstallationObject{StoredFluidLiters=10},sharedPump,true);
  machine.AddRoute(new InstallationObject{StoredFluidLiters=10},sharedPump,true);
  machine.AddRoute(new InstallationObject(),sharedPump,false);
  machine.AddRoute(new InstallationObject(),sharedPump,false);
  Eq(machine.Preview(true,5),.5f,"input preflight sees shared pump allowance, not total reserves");
  Eq(machine.Preview(false,5),.5f,"output preflight sees shared pump allowance, not free tank space");
  Eq(machine.PreviewAcrossPorts(.4f),.5f,"all refinery ports share one planned pump allowance");
  Eq(sharedPump.LimitTransferVolume(5,.1f),.5f,"preflight does not consume pump allowance");
  sharedPump.LimitTransferVolume(5,.1f); sharedPump.RecordTransferredVolume(.5f);
  Eq(machine.Preview(true,5),0,"exhausted input pump blocks material consumption");
  Eq(machine.Preview(false,5),0,"exhausted output pump blocks material consumption");
  Console.WriteLine($"Pump conservation: {passed} passed, {failed} failed."); return failed==0?0:1;
 }
}
