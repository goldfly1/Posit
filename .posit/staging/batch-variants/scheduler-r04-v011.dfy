datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int)
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
  ensures result.value in queue
  ensures forall j :: 0 <= j < |queue| ==> result.value.deadline <= queue[j].deadline
{
  var minIdx := 0;
  var i := 1;
  while i < |queue|
    invariant 0 <= minIdx < i <= |queue|
    invariant forall j :: 0 <= j < i ==> queue[j].deadline >= queue[minIdx].deadline
    decreases |queue| - i
  {
    if queue[i].deadline < queue[minIdx].deadline {
      minIdx := i;
    }
    i := i + 1;
  }
  result := Success(queue[minIdx]);
  newQueue := queue[..minIdx] + queue[minIdx + 1..];
}

method Cancel(queue: seq<Entry>, taskId: int) returns (newQueue: seq<Entry>)
  ensures |newQueue| <= |queue|
  ensures forall i :: 0 <= i < |newQueue| ==> newQueue[i].task.id != taskId
  ensures forall i :: 0 <= i < |newQueue| ==> newQueue[i] in queue
{
  newQueue := [];
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant |newQueue| <= i
    invariant forall j :: 0 <= j < |newQueue| ==> newQueue[j].task.id != taskId
    invariant forall j :: 0 <= j < |newQueue| ==> newQueue[j] in queue[..i]
    decreases |queue| - i
  {
    if queue[i].task.id != taskId {
      newQueue := newQueue + [queue[i]];
    }
    i := i + 1;
  }
}