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
  ensures !result.Success? ==> |newQueue| == |queue|
  ensures !result.Success? ==> forall j :: 0 <= j < |queue| ==> queue[j].delay > 0
{
  var idx := -1;
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant idx == -1 ==> forall j :: 0 <= j < i ==> queue[j].delay > 0
    invariant idx >= 0 ==> (0 <= idx < i && queue[idx].delay == 0 && forall j :: 0 <= j < idx ==> queue[j].delay > 0)
    decreases |queue| - i
  {
    if idx == -1 && queue[i].delay == 0 {
      idx := i;
    }
    i := i + 1;
  }
  if idx == -1 {
    result := Failure("no ready entry");
    newQueue := queue;
  } else {
    result := Success(queue[idx]);
    newQueue := queue[..idx] + queue[idx + 1..];
  }
}