datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, delay: int)

function Tick(queue: seq<Entry>): seq<Entry>
  ensures |Tick(queue)| == |queue|
  decreases |queue|
{
  if |queue| == 0 then []
  else [Entry(queue[0].task, if queue[0].delay > 0 then queue[0].delay - 1 else 0)] + Tick(queue[1..])
}

method Enqueue(queue: seq<Entry>, e: Entry) returns (q: seq<Entry>)
  ensures |q| == |queue| + 1
  ensures q[|queue|] == e
  ensures forall i :: 0 <= i < |queue| ==> q[i] == queue[i]
{
  q := queue + [e];
}

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, q: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures |q| == |queue| - 1
  ensures r.value == queue[0]
  ensures forall i :: 0 <= i < |q| ==> q[i] == queue[i + 1]
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