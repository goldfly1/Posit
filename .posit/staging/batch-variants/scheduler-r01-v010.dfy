datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int, recurring: bool)

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

method Enqueue(queue: seq<Entry>, e: Entry) returns (q: seq<Entry>)
  requires IsSortedAsc(queue)
  ensures |q| == |queue| + 1
  ensures IsSortedAsc(q)
{
  q := InsertSortedAsc(queue, e);
}

method DequeueRecurring(queue: seq<Entry>) returns (r: Result<Entry>, q: seq<Entry>)
  requires |queue| > 0
  requires IsSortedAsc(queue)
  ensures r.Success?
  ensures r.value == queue[0]
  ensures |q| == if queue[0].recurring then |queue| else |queue| - 1
  ensures IsSortedAsc(q)
{
  var front := queue[0];
  r := Success(front);
  if front.recurring {
    q := InsertSortedAsc(queue[1..], front);
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