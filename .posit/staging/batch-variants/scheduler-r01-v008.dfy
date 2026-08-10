datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, deadline: int)

predicate IsSortedAsc(s: seq<Entry>) {
  forall i :: 0 <= i < |s| - 1 ==> s[i].deadline <= s[i + 1].deadline
}

function InsertSortedAsc(sorted: seq<Entry>, e: Entry): seq<Entry>
  requires IsSortedAsc(sorted)
  ensures |InsertSortedAsc(sorted, e)| == |sorted| + 1
  ensures IsSortedAsc(InsertSortedAsc(sorted, e))
  ensures |sorted| == 0 || e.deadline <= sorted[0].deadline ==> InsertSortedAsc(sorted, e)[0] == e
  ensures |sorted| > 0 && e.deadline > sorted[0].deadline ==> InsertSortedAsc(sorted, e)[0] == sorted[0]
  decreases |sorted|
{
  if |sorted| == 0 then [e]
  else if e.deadline <= sorted[0].deadline then [e] + sorted
  else [sorted[0]] + InsertSortedAsc(sorted[1..], e)
}

lemma SortedAscHeadMin(s: seq<Entry>)
  requires IsSortedAsc(s)
  ensures forall j :: 0 <= j < |s| ==> s[0].deadline <= s[j].deadline
  decreases |s|
{
  if |s| <= 1 {
  } else {
    SortedAscHeadMin(s[1..]);
  }
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

method Peek(queue: seq<Entry>) returns (r: Result<Entry>)
  requires |queue| > 0
  requires IsSortedAsc(queue)
  ensures r.Success?
  ensures r.value == queue[0]
  ensures forall j :: 0 <= j < |queue| ==> r.value.deadline <= queue[j].deadline
{
  SortedAscHeadMin(queue);
  r := Success(queue[0]);
}