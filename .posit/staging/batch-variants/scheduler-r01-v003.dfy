datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task)

function RemoveById(queue: seq<Entry>, id: int): seq<Entry>
  ensures |RemoveById(queue, id)| <= |queue|
  decreases |queue|
{
  if |queue| == 0 then []
  else if queue[0].task.id == id then queue[1..]
  else [queue[0]] + RemoveById(queue[1..], id)
}

method Enqueue(queue: seq<Entry>, e: Entry) returns (q: seq<Entry>)
  ensures |q| == |queue| + 1
{
  q := queue + [e];
}

method Cancel(queue: seq<Entry>, id: int) returns (q: seq<Entry>)
  ensures |q| <= |queue|
{
  q := RemoveById(queue, id);
}

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, q: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures |q| == |queue| - 1
  ensures r.value == queue[0]
{
  r := Success(queue[0]);
  q := queue[1..];
}

method Peek(queue: seq<Entry>) returns (r: Result<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures r.value == queue[0]
{
  r := Success(queue[0]);
}