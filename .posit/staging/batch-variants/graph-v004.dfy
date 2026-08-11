// Variant 005: DFS weighted
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

method HasEdge(adj: seq<seq<Edge>>, from: int, to: int) returns (found: bool)
  requires 0 <= from < |adj|
  ensures found ==> exists e :: e in adj[from] && e.to == to
{
  found := false;
  var i := 0;
  while i < |adj[from]|
    invariant 0 <= i <= |adj[from]|
    decreases |adj[from]| - i
  {
    if adj[from][i].to == to { found := true; }
    i := i + 1;
  }
}

function Neighbors(adj: seq<seq<Edge>>, node: int): seq<int>
  requires 0 <= node < |adj|
{
  seq(|adj[node]|, i => adj[node][i].to)
}

function ReachableDFS(adj: seq<seq<Edge>>, start: int, target: int, unvisited: set<int>): bool
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  requires start in unvisited
  requires forall n :: n in unvisited ==> 0 <= n < |adj|
  decreases |unvisited|, 0
{
  if start == target then true
  else ReachableViaNeighbors(adj, target, unvisited - {start}, Neighbors(adj, start))
}

function ReachableViaNeighbors(adj: seq<seq<Edge>>, target: int, unvisited: set<int>, neighbors: seq<int>): bool
  requires 0 <= target < |adj|
  requires forall n :: n in unvisited ==> 0 <= n < |adj|
  decreases |unvisited|, |neighbors|
{
  if |neighbors| == 0 then false
  else if 0 <= neighbors[0] < |adj| && neighbors[0] in unvisited then
    ReachableDFS(adj, neighbors[0], target, unvisited) || ReachableViaNeighbors(adj, target, unvisited, neighbors[1..])
  else
    ReachableViaNeighbors(adj, target, unvisited, neighbors[1..])
}

function Reachable(adj: seq<seq<Edge>>, start: int, target: int): bool
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
{
  ReachableDFS(adj, start, target, set i | 0 <= i < |adj|)
}

method Search(adj: seq<seq<Edge>>, start: int, target: int) returns (result: Result<bool>)
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  ensures result.Success?
  ensures result.value == Reachable(adj, start, target)
{
  result := Success(Reachable(adj, start, target));
}