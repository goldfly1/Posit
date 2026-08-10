datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, recurring: bool)

method Enqueue(queue: seq<Entry>, e: Entry) returns (q: seq<Entry>)
  ensures |q| == |queue| + 1
  ensures q[|queue|] == e
  ensures forall i :: 0 <= i < |queue| ==> q[i] == queue[i]
{
  q := queue + [e];
}

method DequeueRecurring(queue: seq<Entry>) returns (r: Result<Entry>, q: seq<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures r.value == queue[0]
  ensures |q| == if queue[0].recurring then |queue| else |queue| - 1
{
  var front := queue[0];
  r := Success(front);
  if front.recurring {
    q := queue[1..] + [front];
  } else {
    q := queue[1..];
  }
}

method Peek(queue: seq<Entry>) returns (r: Result<Entry>)
  requires |queue| > 0
  ensures r.Success?
  ensures r.value == queue[0]
{
  r := Success(queue[0]);
}

method CountRecurring(queue: seq<Entry>) returns (n: int)
  ensures n >= 0
  ensures n <= |queue|
{
  n := 0;
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant 0 <= n <= i
    decreases |queue| - i
  {
    if queue[i].recurring { n := n + 1; }
    i := i + 1;
  }
}