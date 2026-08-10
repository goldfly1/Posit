datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int, recurring: bool)

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures forall j :: 0 <= j < |queue| ==> r.value.deadline <= queue[j].deadline
  ensures r.value in queue
  ensures if r.value.recurring then |newQueue| == |queue| else |newQueue| == |queue| - 1
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
  var base := queue[..minIdx] + queue[minIdx + 1..];
  if r.value.recurring {
    newQueue := base + [r.value];
  } else {
    newQueue := base;
  }
}