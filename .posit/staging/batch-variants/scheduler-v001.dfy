datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, delay: int)

method Tick(queue: seq<Entry>) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue|
  ensures forall i :: 0 <= i < |queue| ==> newQueue[i].delay == queue[i].delay - 1
{
  newQueue := [];
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant |newQueue| == i
    invariant forall j :: 0 <= j < i ==> newQueue[j].delay == queue[j].delay - 1
    decreases |queue| - i
  {
    newQueue := newQueue + [Entry(queue[i].task, queue[i].delay - 1)];
    i := i + 1;
  }
}

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  requires queue[0].delay <= 0
  ensures r.Success?
  ensures |newQueue| == |queue| - 1
  ensures r.value == queue[0]
{
  r := Success(queue[0]);
  newQueue := queue[1..];
}