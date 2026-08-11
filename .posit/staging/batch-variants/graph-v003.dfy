// Variant 004: DFS basic
include "result.dfy"
datatype Result<T> = Success(value: T) | Failure(error: string)

method AddNode(adj: seq<seq<int>>) returns (newAdj: seq<seq<int>>)
  ensures |newAdj| == |adj| + 1
  ensures newAdj[|adj|] == []
  ensures forall i :: 0 <= i < |adj| ==> newAdj[i] == adj[i]
{
  newAdj := adj + [[]];
}

method AddEdge(adj: seq<seq<int>>, from: int, to: int) returns (newAdj: seq<seq<int>>)
  requires 0 <= from < |adj|
  requires 0 <= to < |adj|
  ensures |newAdj| == |adj|
  ensures forall i :: 0 <= i < |adj| && i != from ==> newAdj[i] == adj[i]
  ensures to in newAdj[from]
{
  newAdj := adj[..from] + [adj[from] + [to]] + adj[from + 1..];
}

method HasEdge(adj: seq<seq<int>>, from: int, to: int) returns (found: bool)
  requires 0 <= from < |adj|
  ensures found ==> exists k :: 0 <= k < |adj[from]| && adj[from][k] == to
{
  found := false;
  var i := 0;
  while i < |adj[from]|
    invariant 0 <= i <= |adj[from]|
    decreases |adj[from]| - i
  {
    if adj[from][i] == to { found := true; }
    i := i + 1;
  }
}

function ReachableDFS(adj: seq<seq<int>>, start: int, target: int, unvisited: set<int>): bool
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  requires start in unvisited
  requires forall n :: n in unvisited ==> 0 <= n < |adj|
  decreases |unvisited|, 0
{
  if start == target then true
  else ReachableViaNeighbors(adj, target, unvisited - {start}, adj[start])
}

function ReachableViaNeighbors(adj: seq<seq<int>>, target: int, unvisited: set<int>, neighbors: seq<int>): bool
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

function Reachable(adj: seq<seq<int>>, start: int, target: int): bool
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
{
  ReachableDFS(adj, start, target, set i | 0 <= i < |adj|)
}

method Search(adj: seq<seq<int>>, start: int, target: int) returns (result: Result<bool>)
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  ensures result.Success?
  ensures result.value == Reachable(adj, start, target)
{
  result := Success(Reachable(adj, start, target));
}