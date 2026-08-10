datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int, delay: int)

predicate IsSortedAsc(s: seq<Entry>) {
  forall i :: 0 <= i < |s| - 1 ==> s[i].deadline <= s[i + 1].deadline
}

function InsertSortedAsc(sorted: seq<Entry>, e: Entry): seq<Entry>
  requires IsSortedAsc(sorted)
  ensures |InsertSortedAsc(sorted, e)| == |sorted| + 1
  ensures IsSortedAsc(InsertSortedAsc(sorted, e))
  decreases |sorted|
{
  if |sorted| == 0 then [e]
  else if e.deadline <= sorted[0].deadline then [e] + sorted
  else [sorted[0]] + InsertSortedAsc(sorted[1..], e)
}

function Tick(queue: seq<Entry>): seq<Entry>
  ensures |Tick(queue)| == |queue|
  decreases |queue|
{
  if |queue| == 0 then []
  else [Entry(queue[0].task, queue[0].deadline, if queue[0].delay > 0 then queue[0].delay - 1 else 0)] + Tick(queue[1..])
}

method Enqueue(queue: seq<Entry>, e: Entry) returns (q: seq<Entry>)
  requires IsSortedAsc(queue)
  ensures |q| == |queue| + 1
  ensures IsSortedAsc(q)
{
  q := InsertSortedAsc(queue, e);
}

method Dequeue(queue: seq<Entry>) returns (r: Result<Entry>, q: seq<Entry>)
  requires |queue| > 0
  requires IsSortedAsc(queue)
  ensures r.Success?
  ensures |q| == |queue| - 1
  ensures r.value == queue[0]
  ensures IsSortedAsc(q)
{
  r := Success(queue[0]);
  q := queue[1..];
}