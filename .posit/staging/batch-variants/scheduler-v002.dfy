datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, recurring: bool)

method Enqueue(queue: seq<Entry>, e: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[|queue|] == e
{
  newQueue := queue + [e];
}

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures r.value == queue[0]
  ensures if r.value.recurring then |newQueue| == |queue| else |newQueue| == |queue| - 1
{
  r := Success(queue[0]);
  if r.value.recurring {
    newQueue := queue[1..] + [queue[0]];
  } else {
    newQueue := queue[1..];
  }
}