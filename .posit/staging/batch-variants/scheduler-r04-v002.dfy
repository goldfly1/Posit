datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, recurring: bool)
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
  ensures !queue[0].recurring ==> |newQueue| == |queue| - 1
  ensures queue[0].recurring ==> |newQueue| == |queue|
  ensures queue[0].recurring ==> newQueue[|queue| - 1] == queue[0]
  ensures forall i :: 0 <= i < |queue| - 1 ==> newQueue[i] == queue[i + 1]
{
  var head := queue[0];
  result := Success(head);
  if head.recurring {
    newQueue := queue[1..] + [head];
  } else {
    newQueue := queue[1..];
  }
}