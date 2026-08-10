datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task)

method Enqueue(queue: seq<Entry>, e: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[|queue|] == e
{
  newQueue := queue + [e];
}

method Cancel(queue: seq<Entry>, id: int) returns (newQueue: seq<Entry>)
  ensures |newQueue| <= |queue|
  ensures forall i :: 0 <= i < |newQueue| ==> newQueue[i].task.id != id
{
  newQueue := [];
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant |newQueue| <= i
    invariant forall j :: 0 <= j < |newQueue| ==> newQueue[j].task.id != id
    decreases |queue| - i
  {
    if queue[i].task.id != id {
      newQueue := newQueue + [queue[i]];
    }
    i := i + 1;
  }
}