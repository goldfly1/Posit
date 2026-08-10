datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int, recurring: bool)

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

method Enqueue(queue: seq<Entry>, e: Entry) returns (q: seq<Entry>)
  requires IsSortedDesc(queue)
  ensures |q| == |queue| + 1
  ensures IsSortedDesc(q)
{
  q := InsertSorted(queue, e);
}

method DequeueRecurring(queue: seq<Entry>) returns (r: Result<Entry>, q: seq<Entry>)
  requires |queue| > 0
  requires IsSortedDesc(queue)
  ensures r.Success?
  ensures r.value == queue[0]
  ensures |q| == if queue[0].recurring then |queue| else |queue| - 1
  ensures IsSortedDesc(q)
{
  var front := queue[0];
  r := Success(front);
  if front.recurring {
    q := InsertSorted(queue[1..], front);
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