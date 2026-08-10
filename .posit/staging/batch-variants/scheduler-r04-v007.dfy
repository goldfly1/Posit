datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int)
datatype Result<T> = Success(value: T) | Failure(error: string)

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
  ensures result.value in queue
  ensures forall j :: 0 <= j < |queue| ==> result.value.priority >= queue[j].priority
{
  var maxIdx := 0;
  var i := 1;
  while i < |queue|
    invariant 0 <= maxIdx < i <= |queue|
    invariant forall j :: 0 <= j < i ==> queue[j].priority <= queue[maxIdx].priority
    decreases |queue| - i
  {
    if queue[i].priority > queue[maxIdx].priority {
      maxIdx := i;
    }
    i := i + 1;
  }
  result := Success(queue[maxIdx]);
  newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
}

method Cancel(queue: seq<Entry>, taskId: int) returns (newQueue: seq<Entry>)
  ensures |newQueue| <= |queue|
  ensures forall i :: 0 <= i < |newQueue| ==> newQueue[i].task.id != taskId
  ensures forall i :: 0 <= i < |newQueue| ==> newQueue[i] in queue
{
  newQueue := [];
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant |newQueue| <= i
    invariant forall j :: 0 <= j < |newQueue| ==> newQueue[j].task.id != taskId
    invariant forall j :: 0 <= j < |newQueue| ==> newQueue[j] in queue[..i]
    decreases |queue| - i
  {
    if queue[i].task.id != taskId {
      newQueue := newQueue + [queue[i]];
    }
    i := i + 1;
  }
}