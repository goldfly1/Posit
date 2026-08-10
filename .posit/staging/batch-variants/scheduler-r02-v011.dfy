datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int, cancelled: bool)

method Enqueue(queue: seq<Entry>, e: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[..|queue|] == queue
{
  newQueue := queue + [e];
}

method Dequeue(queue: seq<Entry>) returns (result: Result<Entry>, newQueue: seq<Entry>)
  ensures |newQueue| <= |queue|
  ensures result.Success? ==> !result.value.cancelled
  ensures result.Success? ==> forall j :: 0 <= j < |queue| && !queue[j].cancelled ==> result.value.deadline <= queue[j].deadline
{
  var minIdx := -1;
  var i := 0;
  while i < |queue|
    invariant -1 <= minIdx < i <= |queue|
    invariant minIdx == -1 || (0 <= minIdx < |queue| && !queue[minIdx].cancelled)
    invariant forall j :: 0 <= j < i && !queue[j].cancelled ==> minIdx != -1 && queue[minIdx].deadline <= queue[j].deadline
    decreases |queue| - i
  {
    if !queue[i].cancelled {
      if minIdx == -1 || queue[i].deadline < queue[minIdx].deadline {
        minIdx := i;
      }
    }
    i := i + 1;
  }
  if minIdx != -1 {
    result := Success(queue[minIdx]);
    newQueue := queue[..minIdx] + queue[minIdx+1..];
  } else {
    result := Failure("all cancelled");
    newQueue := queue;
  }
}

method Peek(queue: seq<Entry>) returns (result: Result<Entry>)
  ensures !result.Success? || !result.value.cancelled
  ensures result.Success? ==> forall j :: 0 <= j < |queue| && !queue[j].cancelled ==> result.value.deadline <= queue[j].deadline
{
  var minIdx := -1;
  var i := 0;
  while i < |queue|
    invariant -1 <= minIdx < i <= |queue|
    invariant minIdx == -1 || (0 <= minIdx < |queue| && !queue[minIdx].cancelled)
    invariant forall j :: 0 <= j < i && !queue[j].cancelled ==> minIdx != -1 && queue[minIdx].deadline <= queue[j].deadline
    decreases |queue| - i
  {
    if !queue[i].cancelled {
      if minIdx == -1 || queue[i].deadline < queue[minIdx].deadline {
        minIdx := i;
      }
    }
    i := i + 1;
  }
  if minIdx != -1 {
    result := Success(queue[minIdx]);
  } else {
    result := Failure("all cancelled");
  }
}