datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, pos: int)

method Enqueue(queue: seq<Entry>, e: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[|queue|] == e
  ensures forall i :: 0 <= i < |queue| ==> newQueue[i] == queue[i]
{
  newQueue := queue + [e];
}

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures |newQueue| == |queue| - 1
  ensures r.value == queue[0]
{
  r := Success(queue[0]);
  newQueue := queue[1..];
}

method Peek(queue: seq<Entry>) returns (r: Result<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures r.value == queue[0]
{
  r := Success(queue[0]);
}