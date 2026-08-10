datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int, recurring: bool)
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
  ensures forall j :: 0 <= j < |queue| ==> result.value.deadline <= queue[j].deadline
  ensures result.value in queue
  ensures |newQueue| == |queue| - 1 + (if result.value.recurring then 1 else 0)
{
  var minIdx := 0;
  var i := 1;
  while i < |queue|
    invariant 0 <= minIdx < i <= |queue|
    invariant forall j :: 0 <= j < i ==> queue[j].deadline >= queue[minIdx].deadline
    decreases |queue| - i
  {
    if queue[i].deadline < queue[minIdx].deadline { minIdx := i; }
    i := i + 1;
  }
  var head := queue[minIdx];
  result := Success(head);
  var rest := queue[..minIdx] + queue[minIdx + 1..];
  if head.recurring {
    newQueue := rest + [Entry(head.task, head.deadline, true)];
  } else {
    newQueue := rest;
  }
}