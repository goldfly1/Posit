datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, delay: int)

predicate IsSortedDesc(s: seq<Entry>) {
  forall i :: 0 <= i < |s| - 1 ==> s[i].priority >= s[i + 1].priority
}

function InsertSorted(sorted: seq<Entry>, e: Entry): seq<Entry>
  requires IsSortedDesc(sorted)
  ensures |InsertSorted(sorted, e)| == |sorted| + 1
  ensures IsSortedDesc(InsertSorted(sorted, e))
  decreases |sorted|
{
  if |sorted| == 0 then [e]
  else if e.priority >= sorted[0].priority then [e] + sorted
  else [sorted[0]] + InsertSorted(sorted[1..], e)
}

function Tick(queue: seq<Entry>): seq<Entry>
  ensures |Tick(queue)| == |queue|
  decreases |queue|
{
  if |queue| == 0 then []
  else [Entry(queue[0].task, queue[0].priority, if queue[0].delay > 0 then queue[0].delay - 1 else 0)] + Tick(queue[1..])
}

method Enqueue(queue: seq<Entry>, e: Entry) returns (q: seq<Entry>)
  requires IsSortedDesc(queue)
  ensures |q| == |queue| + 1
  ensures IsSortedDesc(q)
{
  q := InsertSorted(queue, e);
}

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, q: seq<Entry>)
  requires |queue| > 0
  requires IsSortedDesc(queue)
  ensures r.Success?
  ensures |q| == |queue| - 1
  ensures r.value == queue[0]
  ensures IsSortedDesc(q)
{
  r := Success(queue[0]);
  q := queue[1..];
}