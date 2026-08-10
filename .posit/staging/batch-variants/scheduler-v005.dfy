datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, delay: int)

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  requires exists j :: 0 <= j < |queue| && queue[j].delay <= 0
  ensures r.Success?
  ensures |newQueue| == |queue| - 1
  ensures r.value.delay <= 0
  ensures r.value in queue
  ensures forall j :: 0 <= j < |queue| && queue[j].delay <= 0 ==> r.value.priority >= queue[j].priority
{
  var maxIdx := -1;
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant maxIdx == -1 || (0 <= maxIdx < i && queue[maxIdx].delay <= 0)
    invariant maxIdx == -1 ==> forall j :: 0 <= j < i ==> queue[j].delay > 0
    invariant maxIdx != -1 ==> forall j :: 0 <= j < i && queue[j].delay <= 0 ==> queue[j].priority <= queue[maxIdx].priority
    decreases |queue| - i
  {
    if queue[i].delay <= 0 {
      if maxIdx == -1 || queue[i].priority > queue[maxIdx].priority {
        maxIdx := i;
      }
    }
    i := i + 1;
  }
  r := Success(queue[maxIdx]);
  newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
}