datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int, readyAt: int)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Enqueue(queue: seq<Entry>, entry: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[|queue|] == entry
  ensures forall i :: 0 <= i < |queue| ==> newQueue[i] == queue[i]
{
  newQueue := queue + [entry];
}

method Dequeue(queue: seq<Entry>, now: int) returns (result: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  requires exists i :: 0 <= i < |queue| && queue[i].readyAt <= now
  ensures result.Success?
  ensures |newQueue| == |queue| - 1
  ensures result.value in queue
  ensures result.value.readyAt <= now
  ensures forall j :: 0 <= j < |queue| && queue[j].readyAt <= now ==> result.value.deadline <= queue[j].deadline
{
  var minIdx := -1;
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant minIdx == -1 || 0 <= minIdx < i
    invariant minIdx >= 0 ==> queue[minIdx].readyAt <= now
    invariant minIdx >= 0 ==> forall j :: 0 <= j < i && queue[j].readyAt <= now ==> queue[j].deadline >= queue[minIdx].deadline
    invariant minIdx == -1 ==> forall j :: 0 <= j < i ==> queue[j].readyAt > now
    decreases |queue| - i
  {
    if queue[i].readyAt <= now {
      if minIdx == -1 || queue[i].deadline < queue[minIdx].deadline {
        minIdx := i;
      }
    }
    i := i + 1;
  }
  result := Success(queue[minIdx]);
  newQueue := queue[..minIdx] + queue[minIdx + 1..];
}