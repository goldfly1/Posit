datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, delay: int)
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
  requires forall j :: 0 <= j < |queue| ==> queue[j].delay >= 0
  ensures result.Success? ==> |newQueue| == |queue| - 1
  ensures result.Success? ==> result.value in queue
  ensures result.Success? ==> result.value.delay == 0
  ensures result.Success? ==> forall j :: 0 <= j < |queue| && queue[j].delay == 0 ==> queue[j].priority <= result.value.priority
  ensures !result.Success? ==> |newQueue| == |queue|
  ensures !result.Success? ==> forall j :: 0 <= j < |queue| ==> queue[j].delay > 0
{
  var maxIdx := -1;
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant maxIdx == -1 ==> forall j :: 0 <= j < i ==> queue[j].delay > 0
    invariant maxIdx >= 0 ==> (0 <= maxIdx < i && queue[maxIdx].delay == 0 && forall j :: 0 <= j < i && queue[j].delay == 0 ==> queue[j].priority <= queue[maxIdx].priority)
    decreases |queue| - i
  {
    if queue[i].delay == 0 {
      if maxIdx == -1 || queue[i].priority > queue[maxIdx].priority {
        maxIdx := i;
      }
    }
    i := i + 1;
  }
  if maxIdx == -1 {
    result := Failure("no ready entry");
    newQueue := queue;
  } else {
    result := Success(queue[maxIdx]);
    newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
  }
}