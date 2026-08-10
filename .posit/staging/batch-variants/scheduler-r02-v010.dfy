datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int, recurring: bool)

method Enqueue(queue: seq<Entry>, e: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[..|queue|] == queue
{
  newQueue := queue + [e];
}

method Dequeue(queue: seq<Entry>) returns (result: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures result.Success?
  ensures if result.value.recurring then |newQueue| == |queue| else |newQueue| == |queue| - 1
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
  if result.value.recurring {
    newQueue := queue[..minIdx] + queue[minIdx+1..] + [result.value];
  } else {
    newQueue := queue[..minIdx] + queue[minIdx+1..];
  }
}

method Peek(queue: seq<Entry>) returns (result: Result<Entry>)
  requires |queue| > 0
  ensures result.Success?
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
}