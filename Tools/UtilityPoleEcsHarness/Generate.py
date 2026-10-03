import pathlib, re, sys

root = pathlib.Path(__file__).resolve().parents[2]
source = pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig')
out = pathlib.Path(sys.argv[2])

def member(signature):
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

runtime = 'class UtilityPoleRuntime' in source
pole = 'UtilityPoleRuntime' if runtime else 'UtilityPole'
methods = [
 'private static void RebuildPoleConnections(', 'private static void RebuildPreviewPoleConnections(',
 'private static void RebuildFullPreviewPoleConnections(', 'private static bool HasTopologyReplacementPreview(',
 'private static bool TryAddPoleConnection(PoleConnectionCandidate candidate)',
 'private static bool TryAddPoleConnection(PoleConnectionCandidate candidate,',
 'private static int CountUnconnectedCandidateEndpoints(', 'private static void InitializeConnectionComponents(',
 'private static void ClearConnectionComponents(', 'private static bool ArePolesInSameComponent(',
 'private static void UnionConnectionComponents(', 'private static int FindConnectionComponentRoot(',
 'private static void MarkConnectionLinePointsOccupied(', 'private static float GetPoleDistanceSqr(',
 'private static Vector3 GetPoleRangeCenter(', 'private static bool TryGetBestLinePointDistanceSqr(',
 'private static bool TryResolveClosestAvailableLinePointPair(', 'private static int ComparePoleConnectionCandidates(',
 'private static int CompareDistance(', 'private static void RebuildPoleConnectionAdjacency(',
 'private static void AddConnectedPole(', 'private static void ClearPoleConnectionAdjacency(',
 'private static void BuildSpatialConnectionCandidates(', 'private static Vector2Int GetConnectionSpatialCell(',
 'private static void ClearConnectionPoleSpatialIndex(', 'private static bool ArePolesAutoConnected(',
 'private static int ChebyshevDistance(', 'private void ResetLinePointConnections(',
 'private bool IsLinePointConnectionOccupied(', 'private void SetLinePointConnectionOccupied(',
 'private int GetLinePointConnectionCount(', 'private bool TryGetConnectionLinePoint(',
 'private bool TryGetConnectionLinePointIndex(', 'private readonly struct PoleConnectionCandidate',
 'private readonly struct PoleConnection\n', 'private readonly struct PreviewPoleRuntime',
 'private readonly struct PreviewConsumerRuntime', 'private static bool PoleSuppliesInstalledConsumer(',
 'private static bool PoleSuppliesPreviewConsumer(', 'private static bool IsBetterConsumerPowerLinePole(',
 'private static void TrySelectConsumerPowerLinePole(\n        ' + pole + ' pole,\n        Vector3 consumerPosition,'
]
code = '\n'.join(member(x) for x in methods)
code = re.sub(r'\b' + pole + r'\b', 'PoleHost', code)
code = re.sub(r'\b(?:Transform|UtilityPoleLinePoint)\b', 'Point', code)
code = code.replace('pole.transform.position', 'pole.WorldPosition').replace('pole.gameObject.activeInHierarchy', 'pole.IsRuntimeActive')
# Unity-native authoring and profiler boundaries only; all graph/endpoint decisions are real production code.
code = re.sub(r'using var sample = MapObjectTickProfiler.SampleNamed\(.*?\);', '', code, flags=re.S)
fields = source[source.index('    private static readonly HashSet<'):source.index('    private static long networkRuntimeEvaluatedSimulationTick')]
keep = ['activePoles', 'connectionPoleScratch', 'connectionCandidateScratch', 'connectionPolesByCoordinate',
 'connectionPoleListPool', 'connectionPoleOrder', 'connectionComponentIndex', 'connectionComponentParents',
 'connectionComponentRanks', 'poleConnections', 'previewPoleConnections', 'connectedPolesByPole',
 'connectedPoleListPool', 'previewPoleRuntimes']
fields = '\n'.join(x for x in re.findall(r'    private static readonly .*?;', fields, flags=re.S)
                   if any(re.search(r'\b' + k + r'\s*=', x) for k in keep))
fields = re.sub(r'\b' + pole + r'\b', 'PoleHost', fields)
host = '''using System; using System.Collections.Generic; using System.Security.Cryptography; using System.Text; using UnityEngine;
class Point { public Vector3 Position; }
class InstallationObject {
 public List<Vector2Int> RuntimeOccupiedCoordinates = new();
 public (int mapSizeX,int mapSizeY) Status = (2,3);
 public Vector2Int PlacementCenterCell;
}
static class InputOutputModule {
 public static Vector2Int RotateRectGridOffset(Vector2Int p,int q) => q==0?p:q==1?new Vector2Int(p.y,-p.x):q==2?-p:new Vector2Int(-p.y,p.x);
}
partial class PoleHost {
const int LinePointAIndex=0, LinePointBIndex=1, MaxConnectionsPerLinePoint=2;
const float DistanceTieEpsilon=.0001f;
static bool previewPoleConnectionsDirty;
int linePointAConnectionCount, linePointBConnectionCount;
int ExternalConnectionCount => linePointAConnectionCount+linePointBConnectionCount;
Point linePointCenter, linePointA, linePointB; public Vector3 WorldPosition; public Vector2Int Anchor;
public bool IsRuntimeActive=true; public int ConnectionRadiusCells, SupplyRadiusCells; public int Id;
void ResolveLinePointReferences() { }
Vector3 GetSupplyRangeCenter() => WorldPosition;
static Vector3 ResolveLinePointWorldPosition(Point p) => p.Position;
static bool IsValidPlacedPole(PoleHost p) => p!=null && p.IsRuntimeActive && !IsPreviewPole(p);
static bool IsValidPreviewPole(PoleHost p) => p!=null && p.IsRuntimeActive && IsPreviewPole(p);
static bool IsPreviewPole(PoleHost p) => previewPoleRuntimes.ContainsKey(p);
static bool TryGetPoleAnchorCoordinate(PoleHost p,out Vector2Int v) { v=p.Anchor; return true; }
static int CompareSimulationOrder(PoleHost a,PoleHost b) => a.Id.CompareTo(b.Id);
static void CleanupPreviewPoleRuntimes() { }
public static string Run(int seed) {
 activePoles.Clear(); previewPoleRuntimes.Clear(); var random=new System.Random(seed); int n=32;
 for(int i=0;i<n;i++) {
  var anchor=new Vector2Int(random.Next(-18,19),random.Next(-18,19)); int q=random.Next(4);
  var offset=q==0?new Vector3(.4f,0,0):q==1?new Vector3(0,0,-.4f):q==2?new Vector3(-.4f,0,0):new Vector3(0,0,.4f);
  var p=new PoleHost {Id=i,Anchor=anchor,WorldPosition=new Vector3(anchor.x,0,anchor.y),ConnectionRadiusCells=i%3==0?7:5,SupplyRadiusCells=i%3==0?3:2};
  p.linePointCenter=new Point {Position=p.WorldPosition+Vector3.up*2};
  p.linePointA=new Point {Position=p.WorldPosition+Vector3.up*2+offset};
  p.linePointB=new Point {Position=p.WorldPosition+Vector3.up*2-offset};
  if(i<25) activePoles.Add(p); else previewPoleRuntimes.Add(p,new PreviewPoleRuntime(anchor,q,seed%2==0));
 }
 RebuildPoleConnections(); var text=new StringBuilder(); Append(text,poleConnections);
 RebuildPreviewPoleConnections(); text.Append('|'); Append(text,previewPoleConnections);
 // Rebuild after insertion-order reversal must preserve the exact edges and chosen terminals.
 var reversed=new List<PoleHost>(activePoles); reversed.Reverse(); activePoles.Clear(); foreach(var p in reversed)activePoles.Add(p);
 RebuildPoleConnections(); text.Append('|'); Append(text,poleConnections);
 text.Append('|');
 for(int i=0;i<64;i++) {
  var consumer=new InstallationObject(); var anchor=new Vector2Int(random.Next(-18,19),random.Next(-18,19)); int q=i%4;
  for(int y=0;y<3;y++)for(int x=0;x<2;x++)consumer.RuntimeOccupiedCoordinates.Add(anchor+InputOutputModule.RotateRectGridOffset(new Vector2Int(x,y),q));
  var preview=new PreviewConsumerRuntime(anchor,q,true);
  PoleHost installed=null,ghost=null; float a=float.MaxValue,b=float.MaxValue; var point=new Vector3(anchor.x,2,anchor.y);
  foreach(var p in activePoles) {
   bool supplies=PoleSuppliesInstalledConsumer(p,consumer);
   if(supplies!=PoleSuppliesPreviewConsumer(p,consumer,preview))throw new Exception("Installed and preview supply membership disagree");
   if(supplies) TrySelectConsumerPowerLinePole(p,point,ref installed,ref a);
  }
  foreach(var p in previewPoleRuntimes.Keys)if(PoleSuppliesPreviewConsumer(p,consumer,preview))TrySelectConsumerPowerLinePole(p,point,ref ghost,ref b);
  text.Append(installed?.Id??-1).Append(',').Append(ghost?.Id??-1).Append(';');
 }
 return text.ToString();
}
static void Append(StringBuilder text,List<PoleConnection> edges) {
 var counts=new Dictionary<Point,int>(); var parents=new int[32]; for(int i=0;i<parents.Length;i++)parents[i]=i;
 int Root(int i) { while(parents[i]!=i)i=parents[i]; return i; }
 foreach(var e in edges) {
  foreach(var terminal in new[]{e.FirstPoint,e.SecondPoint}) { counts.TryGetValue(terminal,out int n); if(n>=2)throw new Exception("Terminal capacity exceeded"); counts[terminal]=n+1; }
  int a=Root(e.FirstPole.Id),b=Root(e.SecondPole.Id);if(a==b)throw new Exception("Cycle introduced");parents[b]=a;
  text.Append(e.FirstPole.Id).Append(':').Append(e.FirstPoint==e.FirstPole.linePointA?'A':'B').Append('-')
      .Append(e.SecondPole.Id).Append(':').Append(e.SecondPoint==e.SecondPole.linePointA?'A':'B').Append(';');
 }
}
'''
host += fields + '\n' + code + '\n}\n'
host += '''class Program { static void Main(string[] args) {
 var rows=new List<string>(); for(int seed=0;seed<256;seed++) rows.Add(PoleHost.Run(seed));
 if(args.Length>0 && args[0]=="record") System.IO.File.WriteAllLines(args[1],rows);
 else { var expected=System.IO.File.ReadAllLines(args[0]); for(int i=0;i<rows.Count;i++)
 if(rows[i]!=expected[i])throw new Exception("Wire topology changed at seed "+i+"\\n"+rows[i]+"\\n"+expected[i]); }
 Console.WriteLine("256 installed/preview/rotation/mixed-range wire fixtures passed.");
}}
'''
(out/'Probe.cs').write_text(host, encoding='utf-8')
