datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int)
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
  ensures forall j :: 0 <= j < |queue| ==> result.value.priority >= queue[j].priority
  ensures result.value in queue
{
  var maxIdx := 0;
  var i := 1;
  while i < |queue|
    invariant 0 <= maxIdx < i <= |queue|
    invariant forall j :: 0 <= j < i ==> queue[j].priority <= queue[maxIdx].priority
    decreases |queue| - i
  {
    if queue[i].priority > queue[maxIdx].priority { maxIdx := i; }
    i := i + 1;
  }
  result := Success(queue[maxIdx]);
  newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
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