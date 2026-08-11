// Variant 003: BFS withCycle
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

// Cycle detection using DFS coloring: 0=white,1=gray,2=black
function HasCycleDFS(adj: seq<seq<int>>, node: int, color: seq<int>): bool
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
      if adj[node][i] >= 0 && adj[node][i] < |adj| {
        var child := adj[node][i];
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

function HasCycle(adj: seq<seq<int>>): bool
  requires |adj| > 0
{
  var color := seq(|adj|, i => 0);
  HasCycleDFS(adj, 0, color)
}

method Search(adj: seq<seq<int>>, start: int, target: int) returns (result: Result<bool>)
  requires 0 <= start < |adj|
  requires 0 <= target < |adj|
  ensures result.Success?
  ensures result.value == BFSReachable(adj, start, target)
{
  result := Success(BFSReachable(adj, start, target));
}