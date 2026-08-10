datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int)
datatype Result<T> = Success(value: T) | Failure(error: string)

predicate IsSortedDesc(s: seq<Entry>)
{
  forall i :: 0 <= i < |s| - 1 ==> s[i].priority >= s[i + 1].priority
}

method Enqueue(queue: seq<Entry>, entry: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[|queue|] == entry
  ensures forall i :: 0 <= i < |queue| ==> newQueue[i] == queue[i]
{
  newQueue := queue + [entry];
}

method Dequeue(queue: seq<Entry>) returns (result: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures result.Success?
  ensures |newQueue| == |queue| - 1
  ensures forall j :: 0 <= j < |queue| ==> result.value.priority >= queue[j].priority
  ensures result.value in queue
{
  var maxIdx := 0;
  var i := 1;
  while i < |queue|
    invariant 0 <= maxIdx < i <= |queue|
    invariant forall j :: 0 <= j < i ==> queue[j].priority <= queue[maxIdx].priority
    decreases |queue| - i
  {
    if queue[i].priority > queue[maxIdx].priority { maxIdx := i; }
    i := i + 1;
  }
  result := Success(queue[maxIdx]);
  newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
}

function InsertSorted(sorted: seq<Entry>, e: Entry): seq<Entry>
  requires IsSortedDesc(sorted)
  ensures |InsertSorted(sorted, e)| == |sorted| + 1
  ensures IsSortedDesc(InsertSorted(sorted, e))
  decreases |sorted|
{
  if |sorted| == 0 then [e]
  else if e.priority >= sorted[0].priority then [e] + sorted
  else [sorted[0]] + InsertSorted(sorted[1..], e)
}

method Prioritize(queue: seq<Entry>) returns (sorted: seq<Entry>)
  ensures |sorted| == |queue|
  ensures IsSortedDesc(sorted)
{
  sorted := [];
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant |sorted| == i
    invariant IsSortedDesc(sorted)
    decreases |queue| - i
  {
    sorted := InsertSorted(sorted, queue[i]);
    i := i + 1;
  }
}