datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, readyAt: int)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Enqueue(queue: seq<Entry>, entry: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[|queue|] == entry
  ensures forall i :: 0 <= i < |queue| ==> newQueue[i] == queue[i]
{
  newQueue := queue + [entry];
}

method Dequeue(queue: seq<Entry>, now: int) returns (result: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  requires exists i :: 0 <= i < |queue| && queue[i].readyAt <= now
  ensures result.Success?
  ensures |newQueue| == |queue| - 1
  ensures result.value in queue
  ensures result.value.readyAt <= now
  ensures forall j :: 0 <= j < |queue| && queue[j].readyAt <= now ==> result.value.priority >= queue[j].priority
{
  var maxIdx := -1;
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant maxIdx == -1 || 0 <= maxIdx < i
    invariant maxIdx >= 0 ==> queue[maxIdx].readyAt <= now
    invariant maxIdx >= 0 ==> forall j :: 0 <= j < i && queue[j].readyAt <= now ==> queue[j].priority <= queue[maxIdx].priority
    invariant maxIdx == -1 ==> forall j :: 0 <= j < i ==> queue[j].readyAt > now
    decreases |queue| - i
  {
    if queue[i].readyAt <= now {
      if maxIdx == -1 || queue[i].priority > queue[maxIdx].priority {
        maxIdx := i;
      }
    }
    i := i + 1;
  }
  result := Success(queue[maxIdx]);
  newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
}