using ProjectF.Conveyors;
using UnityEngine;

int checks = 0;
void Require(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}

// Compare components to an independent breadth-first traversal, including removal,
// cycles, one-way joins, duplicates, self links and different edge insertion orders.
var random = new Random(91827);
var graph = new BeltSplitGraph();
for (int sample = 0; sample < 400; sample++)
{
    int count = random.Next(1, 65);
    var edges = new List<(int a, int b)>();
    for (int i = 0; i < count * 2; i++) edges.Add((random.Next(count), random.Next(count)));
    for (int stage = 0; stage < 3; stage++)
    {
        if (stage == 2) edges.RemoveAll(edge => edge.a % 3 == 0 || edge.b % 3 == 0);
        graph.Reset(count);
        if (stage == 1) edges.Reverse();
        foreach (var edge in edges) graph.Connect(edge.a, edge.b);
        var expected = Enumerable.Repeat(-1, count).ToArray();
        int groups = 0;
        for (int seed = 0; seed < count; seed++)
        {
            if (expected[seed] >= 0) continue;
            groups++;
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            expected[seed] = seed;
            while (queue.TryDequeue(out int current))
                foreach (var edge in edges)
                {
                    int neighbor = edge.a == current ? edge.b : edge.b == current ? edge.a : -1;
                    if (neighbor < 0 || expected[neighbor] >= 0) continue;
                    expected[neighbor] = seed;
                    queue.Enqueue(neighbor);
                }
        }
        Require(graph.GroupCount == groups, "component count after rebuild");
        for (int i = 0; i < count; i++)
            Require(graph.Representative(i) == expected[i], "canonical component after rebuild");
    }
}
graph.Reset(0);
Require(graph.GroupCount == 0, "world reset releases components");
graph.Reset(100_000);
for (int i = 99_999; i > 0; i--) graph.Connect(i, i - 1);
Require(graph.GroupCount == 1 && graph.Representative(99_999) == 0, "large reverse chain");

// Exercise production lane routing and graph construction; only engine geometry is stubbed.
var connections = new List<(Block block, int lane)>();
Dictionary<(Block block, int lane), int> Rebuild(params Block[] blocks)
{
    var indices = new Dictionary<(Block, int), int>();
    foreach (Block block in blocks)
        for (int lane = 0; lane < block.BeltSplitLaneLimit; lane++)
            if (block.IsBeltSplitLane(lane)) indices.Add((block,lane),indices.Count);
    graph.Reset(indices.Count);
    foreach (var entry in indices)
    {
        connections.Clear();
        entry.Key.Item1.AppendBeltSplitConnections(entry.Key.Item2, connections);
        foreach (var target in connections)
            if (indices.TryGetValue(target, out int index)) graph.Connect(entry.Value,index);
    }
    return indices;
}
var terrain = new TerrainGenerator();
var bridge = new ConvayorBelt2F();
var upperIn = new Block { coordinate = new(0,-1), Bridge=bridge, Terrain=terrain };
var center = new Block { coordinate = new(0,0), CenterBridge=bridge, Terrain=terrain };
var upperOut = new Block { coordinate = new(0,1), Bridge=bridge, Terrain=terrain };
var lowerIn = new Block { coordinate = new(-1,0), Terrain=terrain };
var lowerOut = new Block { coordinate = new(1,0), Terrain=terrain };
Block[] crossing = { upperIn, center, upperOut, lowerIn, lowerOut };
foreach (Block block in crossing) terrain.Blocks.Add(block.Coordinate,block);
foreach (Block block in new[] {upperIn,center,upperOut}) bridge.Cells.Add(block.Coordinate);
upperIn.Next=center; lowerIn.Next=center; center.Next=lowerOut;
var nodes = Rebuild(crossing);
Require(graph.GroupCount==2, "crossing in the same Block must remain TWO transport groups");
Require(graph.Representative(nodes[(center,0)]) != graph.Representative(nodes[(center,1)]), "upper and lower front slots are separate");
Require(graph.Representative(nodes[(upperIn,2)]) == graph.Representative(nodes[(upperOut,0)]), "upper entry, center and exit connected");
Require(graph.Representative(nodes[(lowerIn,2)]) == graph.Representative(nodes[(lowerOut,0)]), "ground path through crossing connected");
Require(!upperIn.ResolveSimulation(0), "normal routing retains storage validation");
foreach (Block block in crossing) block.StorageAvailable=true;
Require(upperIn.ResolveSimulation(0), "normal routing still works with initialized storage");
nodes=Rebuild(crossing);
Require(graph.GroupCount==2, "storage initialization cannot alter groups");
upperOut.Next=lowerOut;
Rebuild(crossing);
Require(graph.GroupCount==1, "real external connection merges crossing paths");
upperOut.Next=null;
Rebuild(crossing);
Require(graph.GroupCount==2, "removing external connection separates paths again");
terrain.Blocks.Remove(upperOut.Coordinate);
Rebuild(upperIn,center,lowerIn,lowerOut);
Require(graph.GroupCount==2, "missing bridge exit must not merge upper lane into ground path");

var left=new Block(); var right=new Block();
var splitter=new Spliterbelt { Left=left, Right=right };
left.Splitter=right.Splitter=splitter;
Rebuild(left,right);
Require(graph.GroupCount==1, "both possible splitter outputs connect the two channels");
left.Splitter=right.Splitter=null;
Rebuild(left,right);
Require(graph.GroupCount==2, "mere adjacency/shared coordinates do not join belts");
left.Next=right;right.SideReceive=true;
nodes=Rebuild(left,right);
Require(graph.GroupCount==1 && nodes.ContainsKey((left,1)), "side input approach participates in the same line");
right.Corner=true;
Rebuild(left,right);
Require(graph.GroupCount==1, "corner receiving lane connects without item storage");
var positions=new List<Vector3>();
center.AppendBeltSplitVisualSegments(3,positions);
Require(positions.Count==2 && positions[0].y==1 && positions[1].y==1, "upper overlay follows upper lane only");
positions.Clear();center.AppendBeltSplitVisualSegments(2,positions);
Require(positions.Count==2 && positions[0].y==0 && positions[1].y==0, "ground overlay follows ground lane only");
Console.WriteLine($"PASS: {checks} belt split checks (production graph, adapter and lane routing).");
