// Variant 011: Dijkstra withCycle
include "result.dfy"
datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Edge = Edge(to: int, weight: int)

method AddNode(adj: seq<seq<Edge>>) returns (newAdj: seq<seq<Edge>>)
  ensures |newAdj| == |adj| + 1
  ensures newAdj[|adj|] == []
  ensures forall i :: 0 <= i < |adj| ==> newAdj[i] == adj[i]
{
  newAdj := adj + [[]];
}

method AddEdge(adj: seq<seq<Edge>>, from: int, to: int, w: int) returns (newAdj: seq<seq<Edge>>)
  requires 0 <= from < |adj|
  requires 0 <= to < |adj|
  ensures |newAdj| == |adj|
  ensures forall i :: 0 <= i < |adj| && i != from ==> newAdj[i] == adj[i]
  ensures exists e :: e in newAdj[from] && e.to == to && e.weight == w
{
  newAdj := adj[..from] + [adj[from] + [Edge(to, w)]] + adj[from + 1..];
}

method FindMin(unvisited: set<int>, dist: seq<int>) returns (minNode: int)
  requires unvisited != {}
  requires forall i :: 0 <= i < |dist| ==> dist[i] >= 0
  ensures minNode in unvisited
  ensures forall i :: i in unvisited ==> dist[minNode] <= dist[i]
{
  var min := -1;
  var minDist := 1000000;
  var i := 0;
  while i < |dist|
    invariant 0 <= i <= |dist|
    invariant min == -1 || (min in unvisited && dist[min] == minDist)
    invariant forall j :: 0 <= j < i && j in unvisited ==> dist[min] <= dist[j]
    decreases |dist| - i
  {
    if i in unvisited {
      if min == -1 || dist[i] < minDist {
        min := i;
        minDist := dist[i];
      }
    }
    i := i + 1;
  }
  minNode := min;
}

method DijkstraReachable(adj: seq<seq<Edge>>, start: int, target: int) returns (found: bool)
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  ensures found == (target == start || exists path)
{
  var dist := seq(|adj|, i => 1000000);
  dist := dist[..start] + [0] + dist[start+1..];
  var visited := {};
  var unvisited := set i | 0 <= i < |adj|;
  while unvisited != {}
    invariant visited * unvisited == {}
    invariant forall i :: 0 <= i < |dist| ==> dist[i] >= 0
    invariant forall i :: i in visited ==> i !in unvisited
    decreases |unvisited|
  {
    var u := FindMin(unvisited, dist);
    if u == target { found := true; return; }
    visited := visited + {u};
    unvisited := unvisited - {u};
    for each e in adj[u] {
      var v := e.to;
      if v !in visited {
        var nd := dist[u] + e.weight;
        if nd < dist[v] {
          dist := dist[..v] + [nd] + dist[v+1..];
        }
      }
    }
  }
  found := false;
}

// Cycle detection (ignoring weights)
function HasCycleDFS(adj: seq<seq<Edge>>, node: int, color: seq<int>): bool
  requires 0 <= node < |adj|
  requires |color| == |adj|
  requires forall i :: 0 <= i < |color| ==> color[i] == 0 || color[i] == 1 || color[i] == 2
  decreases |adj| - (sum i | 0 <= i < |color| && color[i] != 0)
{
  if color[node] == 1 then true
  else if color[node] == 2 then false
  else
    var newColor := color[..node] + [1] + color[node+1..];
    var hasCycle := false;
    var i := 0;
    while i < |adj[node]|
      invariant 0 <= i <= |adj[node]|
      invariant !hasCycle
      decreases |adj[node]| - i
    {
      var e := adj[node][i];
      if e.to >= 0 && e.to < |adj| {
        var child := e.to;
        if newColor[child] == 0 {
          if HasCycleDFS(adj, child, newColor) {
            hasCycle := true;
          }
        } else if newColor[child] == 1 {
          hasCycle := true;
        }
      }
      i := i + 1;
    }
    hasCycle || HasCycleDFS(adj, node, newColor[..node] + [2] + newColor[node+1..])
}

function HasCycle(adj: seq<seq<Edge>>): bool
  requires |adj| > 0
{
  var color := seq(|adj|, i => 0);
  HasCycleDFS(adj, 0, color)
}

method Search(adj: seq<seq<Edge>>, start: int, target: int) returns (result: Result<bool>)
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  ensures result.Success?
  ensures result.value == (target == start || exists path)
{
  var f := DijkstraReachable(adj, start, target);
  result := Success(f);
}