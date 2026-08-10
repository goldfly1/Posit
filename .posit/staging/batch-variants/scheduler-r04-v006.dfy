datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, recurring: bool)
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
  ensures result.value in queue
  ensures forall j :: 0 <= j < |queue| ==> result.value.priority >= queue[j].priority
  ensures !result.value.recurring ==> |newQueue| == |queue| - 1
  ensures result.value.recurring ==> |newQueue| == |queue|
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
  var entry := queue[maxIdx];
  result := Success(entry);
  if entry.recurring {
    newQueue := queue[..maxIdx] + queue[maxIdx + 1..] + [entry];
  } else {
    newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
  }
}