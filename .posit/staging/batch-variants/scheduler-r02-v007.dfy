datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, cancelled: bool)

method Enqueue(queue: seq<Entry>, e: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[..|queue|] == queue
{
  newQueue := queue + [e];
}

method Dequeue(queue: seq<Entry>) returns (result: Result<Entry>, newQueue: seq<Entry>)
  ensures |newQueue| <= |queue|
  ensures result.Success? ==> !result.value.cancelled
  ensures result.Success? ==> forall j :: 0 <= j < |queue| && !queue[j].cancelled ==> result.value.priority >= queue[j].priority
{
  var maxIdx := -1;
  var i := 0;
  while i < |queue|
    invariant -1 <= maxIdx < i <= |queue|
    invariant maxIdx == -1 || (0 <= maxIdx < |queue| && !queue[maxIdx].cancelled)
    invariant forall j :: 0 <= j < i && !queue[j].cancelled ==> maxIdx != -1 && queue[maxIdx].priority >= queue[j].priority
    decreases |queue| - i
  {
    if !queue[i].cancelled {
      if maxIdx == -1 || queue[i].priority > queue[maxIdx].priority {
        maxIdx := i;
      }
    }
    i := i + 1;
  }
  if maxIdx != -1 {
    result := Success(queue[maxIdx]);
    newQueue := queue[..maxIdx] + queue[maxIdx+1..];
  } else {
    result := Failure("all cancelled");
    newQueue := queue;
  }
}

method Peek(queue: seq<Entry>) returns (result: Result<Entry>)
  ensures !result.Success? || !result.value.cancelled
  ensures result.Success? ==> forall j :: 0 <= j < |queue| && !queue[j].cancelled ==> result.value.priority >= queue[j].priority
{
  var maxIdx := -1;
  var i := 0;
  while i < |queue|
    invariant -1 <= maxIdx < i <= |queue|
    invariant maxIdx == -1 || (0 <= maxIdx < |queue| && !queue[maxIdx].cancelled)
    invariant forall j :: 0 <= j < i && !queue[j].cancelled ==> maxIdx != -1 && queue[maxIdx].priority >= queue[j].priority
    decreases |queue| - i
  {
    if !queue[i].cancelled {
      if maxIdx == -1 || queue[i].priority > queue[maxIdx].priority {
        maxIdx := i;
      }
    }
    i := i + 1;
  }
  if maxIdx != -1 {
    result := Success(queue[maxIdx]);
  } else {
    result := Failure("all cancelled");
  }
}