// Variant 002: BFS directed (parameter directed for edge addition)
include "result.dfy"
datatype Result<T> = Success(value: T) | Failure(error: string)

method AddNode(adj: seq<seq<int>>) returns (newAdj: seq<seq<int>>)
  ensures |newAdj| == |adj| + 1
  ensures newAdj[|adj|] == []
  ensures forall i :: 0 <= i < |adj| ==> newAdj[i] == adj[i]
{
  newAdj := adj + [[]];
}

method AddEdge(adj: seq<seq<int>>, from: int, to: int, directed: bool) returns (newAdj: seq<seq<int>>)
  requires 0 <= from < |adj|
  requires 0 <= to < |adj|
  ensures |newAdj| == |adj|
  ensures forall i :: 0 <= i < |adj| && i != from ==> newAdj[i] == adj[i]
  ensures to in newAdj[from]
  ensures !directed ==> from in newAdj[to]
{
  var rev := if directed then [] else [from];
  newAdj := adj[..from] + [adj[from] + [to]] + adj[from + 1..];
  if !directed {
    newAdj := newAdj[..to] + [newAdj[to] + rev] + newAdj[to + 1..];
  }
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

function NextFrontier(adj: seq<seq<int>>, frontier: set<int>, visited: set<int>): set<int>
  requires forall n :: n in frontier ==> 0 <= n < |adj|
  requires forall n :: n in visited ==> 0 <= n < |adj|
  ensures forall n :: n in NextFrontier(adj, frontier, visited) ==> 0 <= n < |adj|
  ensures forall n :: n in NextFrontier(adj, frontier, visited) ==> n !in visited && n !in frontier
{
  set n | 0 <= n < |adj| && n !in visited && n !in frontier && exists m :: m in frontier && n in adj[m]
}

function BFSLevel(adj: seq<seq<int>>, target: int, frontier: set<int>, visited: set<int>): bool
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

function BFSReachable(adj: seq<seq<int>>, start: int, target: int): bool
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
{
  BFSLevel(adj, target, {start}, {})
}

method Search(adj: seq<seq<int>>, start: int, target: int) returns (result: Result<bool>)
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  ensures result.Success?
  ensures result.value == BFSReachable(adj, start, target)
{
  result := Success(BFSReachable(adj, start, target));
}