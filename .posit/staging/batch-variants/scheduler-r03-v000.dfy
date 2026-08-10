datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, arrival: int)
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
  ensures result.Success?
  ensures |newQueue| == |queue| - 1
  ensures result.value == queue[0]
  ensures forall i :: 0 <= i < |newQueue| ==> newQueue[i] == queue[i + 1]
{
  result := Success(queue[0]);
  newQueue := queue[1..];
}

method Peek(queue: seq<Entry>) returns (result: Result<Entry>)
  requires |queue| > 0
  ensures result.Success?
  ensures result.value == queue[0]
{
  result := Success(queue[0]);
}