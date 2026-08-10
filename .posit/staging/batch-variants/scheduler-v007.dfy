datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int)

method Cancel(queue: seq<Entry>, id: int) returns (newQueue: seq<Entry>)
  ensures |newQueue| <= |queue|
  ensures forall i :: 0 <= i < |newQueue| ==> newQueue[i].task.id != id
{
  newQueue := [];
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant |newQueue| <= i
    invariant forall j :: 0 <= j < |newQueue| ==> newQueue[j].task.id != id
    decreases |queue| - i
  {
    if queue[i].task.id != id {
      newQueue := newQueue + [queue[i]];
    }
    i := i + 1;
  }
}

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures |newQueue| == |queue| - 1
  ensures forall j :: 0 <= j < |queue| ==> r.value.priority >= queue[j].priority
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
  r := Success(queue[maxIdx]);
  newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
}