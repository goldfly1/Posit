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
  ensures forall j :: 0 <= j < |queue| ==> result.value.priority >= queue[j].priority
  ensures result.value in queue
  ensures |newQueue| == |queue| - 1 + (if result.value.recurring then 1 else 0)
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
  var head := queue[maxIdx];
  result := Success(head);
  var rest := queue[..maxIdx] + queue[maxIdx + 1..];
  if head.recurring {
    newQueue := rest + [Entry(head.task, head.priority, true)];
  } else {
    newQueue := rest;
  }
}