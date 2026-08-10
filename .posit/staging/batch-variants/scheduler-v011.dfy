datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int)

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

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures |newQueue| == |queue| - 1
  ensures forall j :: 0 <= j < |queue| ==> r.value.deadline <= queue[j].deadline
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
  r := Success(queue[minIdx]);
  newQueue := queue[..minIdx] + queue[minIdx + 1..];
}