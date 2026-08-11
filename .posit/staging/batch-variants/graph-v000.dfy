// Variant 001: BFS weighted
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

function NextFrontier(adj: seq<seq<Edge>>, frontier: set<int>, visited: set<int>): set<int>
  requires forall n :: n in frontier ==> 0 <= n < |adj|
  requires forall n :: n in visited ==> 0 <= n < |adj|
  ensures forall n :: n in NextFrontier(adj, frontier, visited) ==> 0 <= n < |adj|
  ensures forall n :: n in NextFrontier(adj, frontier, visited) ==> n !in visited && n !in frontier
{
  set n | 0 <= n < |adj| && n !in visited && n !in frontier && exists m :: m in frontier && n in Neighbors(adj, m)
}

function BFSLevel(adj: seq<seq<Edge>>, target: int, frontier: set<int>, visited: set<int>): bool
  requires 0 <= target < |adj|
  requires forall n :: n in frontier ==> 0 <= n < |adj|
  requires forall n :: n in visited ==> 0 <= n < |adj|
  requires frontier * visited == {}
  decreases |adj| - |visited|
{
  if target in frontier then true
  else if frontier == {} then false
  else
    var next := NextFrontier(adj, frontier, visited);
    BFSLevel(adj, target, next, visited + frontier)
}

function BFSReachable(adj: seq<seq<Edge>>, start: int, target: int): bool
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
{
  BFSLevel(adj, target, {start}, {})
}

method Search(adj: seq<seq<Edge>>, start: int, target: int) returns (result: Result<bool>)
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  ensures result.Success?
  ensures result.value == BFSReachable(adj, start, target)
{
  result := Success(BFSReachable(adj, start, target));
}