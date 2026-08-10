datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, recurring: bool)

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures forall j :: 0 <= j < |queue| ==> r.value.priority >= queue[j].priority
  ensures r.value in queue
  ensures if r.value.recurring then |newQueue| == |queue| else |newQueue| == |queue| - 1
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
  var base := queue[..maxIdx] + queue[maxIdx + 1..];
  if r.value.recurring {
    newQueue := base + [r.value];
  } else {
    newQueue := base;
  }
}