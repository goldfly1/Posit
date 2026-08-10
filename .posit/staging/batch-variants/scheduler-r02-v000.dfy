datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)

method Enqueue(queue: seq<Task>, t: Task) returns (newQueue: seq<Task>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[..|queue|] == queue
{
  newQueue := queue + [t];
}

method Dequeue(queue: seq<Task>) returns (result: Result<Task>, newQueue: seq<Task>)
  requires |queue| > 0
  ensures result.Success?
  ensures result.value == queue[0]
  ensures |newQueue| == |queue| - 1
  ensures newQueue == queue[1..]
{
  result := Success(queue[0]);
  newQueue := queue[1..];
}

method Peek(queue: seq<Task>) returns (result: Result<Task>)
  requires |queue| > 0
  ensures result.Success?
  ensures result.value == queue[0]
{
  result := Success(queue[0]);
}