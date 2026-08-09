// Pattern: Scheduler (Approach 3 — pre-written body with parameters)
// responsibility: Enqueue, dequeue, and prioritize tasks
// test: Dequeue([Entry(Task(1, "a"), 5), Entry(Task(2, "b"), 10)]) returns Success(Entry(Task(2, "b"), 10))
// test: Dequeue([Entry(Task(1, "a"), 5)]) returns Success(Entry(Task(1, "a"), 5))
// test: Peek([Entry(Task(1, "a"), 5), Entry(Task(2, "b"), 10)]) returns Success(Entry(Task(2, "b"), 10))
// test: Prioritize([Entry(Task(1, "a"), 5), Entry(Task(2, "b"), 10)]) returns [Entry(Task(2, "b"), 10), Entry(Task(1, "a"), 5)]
//
// Parameters:
//   policy: string — "priority" (highest first) or "fifo" (first in first out)
//   maxQueueSize: int — maximum queue depth (0 = unlimited)

include "result.dfy"

datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, priority: int)

// Predicate: sequence is sorted in descending priority order
predicate IsSortedDesc(s: seq<Entry>)
{
  forall i :: 0 <= i < |s| - 1 ==> s[i].priority >= s[i + 1].priority
}

// Enqueue: add an entry to the end of the queue
method Enqueue(queue: seq<Entry>, entry: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[|queue|] == entry
  ensures forall i :: 0 <= i < |queue| ==> newQueue[i] == queue[i]
{
  newQueue := queue + [entry];
}

// Dequeue: remove and return the highest-priority entry
method Dequeue(queue: seq<Entry>) returns (result: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  ensures result.Success?
  ensures |newQueue| == |queue| - 1
  ensures forall j :: 0 <= j < |queue| ==> result.value.priority >= queue[j].priority
  ensures result.value in queue
  decreases |queue|
{
  var maxIdx := 0;
  var i := 1;
  while i < |queue|
    invariant 0 <= maxIdx < i <= |queue|
    invariant forall j :: 0 <= j < i ==> queue[j].priority <= queue[maxIdx].priority
    decreases |queue| - i
  {
    if queue[i].priority > queue[maxIdx].priority {
      maxIdx := i;
    }
    i := i + 1;
  }
  result := Success(queue[maxIdx]);
  newQueue := queue[..maxIdx] + queue[maxIdx + 1..];
}

// Peek: return the highest-priority entry without removing it
method Peek(queue: seq<Entry>) returns (result: Result<Entry>)
  requires |queue| > 0
  ensures result.Success?
  ensures forall j :: 0 <= j < |queue| ==> result.value.priority >= queue[j].priority
  ensures result.value in queue
  decreases |queue|
{
  var maxIdx := 0;
  var i := 1;
  while i < |queue|
    invariant 0 <= maxIdx < i <= |queue|
    invariant forall j :: 0 <= j < i ==> queue[j].priority <= queue[maxIdx].priority
    decreases |queue| - i
  {
    if queue[i].priority > queue[maxIdx].priority {
      maxIdx := i;
    }
    i := i + 1;
  }
  result := Success(queue[maxIdx]);
}

// Insert an entry into a sorted queue, maintaining descending priority order
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

// Prioritize: sort the queue by priority (highest first) using insertion sort
method Prioritize(queue: seq<Entry>) returns (sorted: seq<Entry>)
  ensures |sorted| == |queue|
  ensures IsSortedDesc(sorted)
  decreases |queue|
{
  sorted := [];
  var i := 0;
  while i < |queue|
    invariant 0 <= i <= |queue|
    invariant |sorted| == i
    invariant IsSortedDesc(sorted)
    decreases |queue| - i
  {
    sorted := InsertSorted(sorted, queue[i]);
    i := i + 1;
  }
}