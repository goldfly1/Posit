datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, arrival: int, recurring: bool)
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
  ensures result.value == queue[0]
  ensures |newQueue| == |queue| - 1 + (if result.value.recurring then 1 else 0)
  ensures forall i :: 0 <= i < |queue| - 1 ==> newQueue[i] == queue[i + 1]
{
  var head := queue[0];
  result := Success(head);
  var rest := queue[1..];
  if head.recurring {
    newQueue := rest + [Entry(head.task, head.arrival + 1, true)];
  } else {
    newQueue := rest;
  }
}

method Peek(queue: seq<Entry>) returns (result: Result<Entry>)
  requires |queue| > 0
  ensures result.Success?
  ensures result.value == queue[0]
{
  result := Success(queue[0]);
}