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
  ensures forall j :: 0 <= j < |queue| ==> result.value.deadline <= queue[j].deadline
  ensures result.value in queue
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
  result := Success(queue[minIdx]);
  newQueue := queue[..minIdx] + queue[minIdx + 1..];
}

function FindById(queue: seq<Entry>, id: int): int
  decreases |queue|
  ensures FindById(queue, id) == -1 || 0 <= FindById(queue, id) < |queue|
  ensures FindById(queue, id) >= 0 ==> queue[FindById(queue, id)].task.id == id
  ensures FindById(queue, id) == -1 ==> forall i :: 0 <= i < |queue| ==> queue[i].task.id != id
{
  if |queue| == 0 then -1
  else if queue[0].task.id == id then 0
  else
    var rest := FindById(queue[1..], id);
    if rest == -1 then -1 else rest + 1
}

method Cancel(queue: seq<Entry>, id: int) returns (newQueue: seq<Entry>, found: bool)
  ensures found ==> |newQueue| == |queue| - 1
  ensures !found ==> newQueue == queue
  ensures !found ==> forall i :: 0 <= i < |queue| ==> queue[i].task.id != id
{
  var idx := FindById(queue, id);
  if idx >= 0 {
    found := true;
    newQueue := queue[..idx] + queue[idx + 1..];
  } else {
    found := false;
    newQueue := queue;
  }
}