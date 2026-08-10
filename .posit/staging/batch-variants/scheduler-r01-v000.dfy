datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task)

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

method Size(queue: seq<Entry>) returns (n: int)
  ensures n == |queue|
  ensures n >= 0
{
  n := |queue|;
}