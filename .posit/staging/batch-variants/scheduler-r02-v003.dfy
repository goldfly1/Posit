datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string, cancelled: bool)

method Enqueue(queue: seq<Task>, t: Task) returns (newQueue: seq<Task>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[..|queue|] == queue
{
  newQueue := queue + [t];
}

method Dequeue(queue: seq<Task>) returns (result: Result<Task>, newQueue: seq<Task>)
  ensures |newQueue| <= |queue|
  ensures result.Success? ==> !result.value.cancelled
{
  result := Failure("");
  newQueue := queue;
  var i := 0;
  var found := false;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant !found ==> result == Failure("")
    invariant found ==> result.Success? && !result.value.cancelled
    invariant !found ==> newQueue == queue
    invariant found ==> newQueue == queue[..i] + queue[i+1..]
    decreases |queue| - i
  {
    if !queue[i].cancelled {
      result := Success(queue[i]);
      newQueue := queue[..i] + queue[i+1..];
      found := true;
    } else {
      i := i + 1;
    }
    if found {
      break;
    }
  }
  if !found {
    result := Failure("all cancelled");
    newQueue := queue;
  }
}

method Peek(queue: seq<Task>) returns (result: Result<Task>)
  ensures !result.Success? || !result.value.cancelled
{
  result := Failure("");
  var i := 0;
  var found := false;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant !found ==> result == Failure("")
    invariant found ==> result.Success? && !result.value.cancelled
    decreases |queue| - i
  {
    if !queue[i].cancelled {
      result := Success(queue[i]);
      found := true;
    } else {
      i := i + 1;
    }
    if found {
      break;
    }
  }
  if !found {
    result := Failure("all cancelled");
  }
}