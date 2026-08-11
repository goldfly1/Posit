// Variant 009: Dijkstra weighted (same as basic but explicit weights)
// Already implemented in variant 008; keep identical for this variant.
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

method Search(adj: seq<seq<Edge>>, start: int, target: int) returns (result: Result<bool>)
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  ensures result.Success?
  ensures result.value == (target == start || exists path)
{
  var f := DijkstraReachable(adj, start, target);
  result := Success(f);
}