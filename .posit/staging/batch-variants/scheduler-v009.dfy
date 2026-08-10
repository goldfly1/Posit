datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int, delay: int)

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  requires exists j :: 0 <= j < |queue| && queue[j].delay <= 0
  ensures r.Success?
  ensures |newQueue| == |queue| - 1
  ensures r.value.delay <= 0
  ensures r.value in queue
  ensures forall j :: 0 <= j < |queue| && queue[j].delay <= 0 ==> r.value.deadline <= queue[j].deadline
{
  var minIdx := -1;
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant minIdx == -1 || (0 <= minIdx < i && queue[minIdx].delay <= 0)
    invariant minIdx == -1 ==> forall j :: 0 <= j < i ==> queue[j].delay > 0
    invariant minIdx != -1 ==> forall j :: 0 <= j < i && queue[j].delay <= 0 ==> queue[j].deadline >= queue[minIdx].deadline
    decreases |queue| - i
  {
    if queue[i].delay <= 0 {
      if minIdx == -1 || queue[i].deadline < queue[minIdx].deadline {
        minIdx := i;
      }
    }
    i := i + 1;
  }
  r := Success(queue[minIdx]);
  newQueue := queue[..minIdx] + queue[minIdx + 1..];
}