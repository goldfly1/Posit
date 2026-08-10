datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int)

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

function RemoveById(sorted: seq<Entry>, id: int): seq<Entry>
  requires IsSortedDesc(sorted)
  ensures |RemoveById(sorted, id)| <= |sorted|
  ensures IsSortedDesc(RemoveById(sorted, id))
  decreases |sorted|
{
  if |sorted| == 0 then []
  else if sorted[0].task.id == id then sorted[1..]
  else [sorted[0]] + RemoveById(sorted[1..], id)
}

method Enqueue(queue: seq<Entry>, e: Entry) returns (q: seq<Entry>)
  requires IsSortedDesc(queue)
  ensures |q| == |queue| + 1
  ensures IsSortedDesc(q)
{
  q := InsertSorted(queue, e);
}

method Cancel(queue: seq<Entry>, id: int) returns (q: seq<Entry>)
  requires IsSortedDesc(queue)
  ensures |q| <= |queue|
  ensures IsSortedDesc(q)
{
  q := RemoveById(queue, id);
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