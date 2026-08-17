// Cut-out: priority-queue
// Pattern: queue (conforms to transformer pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)
// responsibility: manage a priority queue of tasks
// test: Enqueue([], 1, "task1") returns [["1","task1"]]
// test: ListAll([["1","task1"],["2","task2"]]) returns ["task1","task2"]

module PriorityQueue {

// Convert non-negative int to string
function IntToString(n: int): (s: string)
  requires n >= 0
  decreases n
{
  if n < 10 then ["0123456789"[n]]
  else IntToString(n / 10) + ["0123456789"[n % 10]]
}

// Convert digit char to int value
function DigitValue(c: char): (v: int)
  requires '0' <= c <= '9'
{
  if c == '0' then 0
  else if c == '1' then 1
  else if c == '2' then 2
  else if c == '3' then 3
  else if c == '4' then 4
  else if c == '5' then 5
  else if c == '6' then 6
  else if c == '7' then 7
  else if c == '8' then 8
  else 9
}

// Convert string of digits to int
function StringToInt(s: string): (n: int)
  requires forall i :: 0 <= i < |s| ==> '0' <= s[i] <= '9'
  decreases |s|
{
  if |s| == 0 then 0
  else StringToInt(s[0..|s|-1]) * 10 + DigitValue(s[|s|-1])
}

// Enqueue a task with given priority
// Returns items with [priority, task] appended
method Enqueue(items: seq<seq<string>>, priority: int, task: string) returns (result: seq<seq<string>>)
  requires priority >= 0
  ensures |result| == |items| + 1
  decreases |items|
{
  result := items + [[IntToString(priority), task]];
}

// Dequeue the highest-priority (lowest number) task
// Returns the task string and the remaining items
method Dequeue(items: seq<seq<string>>) returns (item: string, rest: seq<seq<string>>)
  requires |items| > 0
  requires forall i :: 0 <= i < |items| ==> |items[i]| >= 2
  requires forall i :: 0 <= i < |items| ==> (forall j :: 0 <= j < |items[i][0]| ==> '0' <= items[i][0][j] <= '9')
  ensures |rest| == |items| - 1
  decreases |items|
{
  var minIdx := 0;
  var minVal := StringToInt(items[0][0]);
  var i := 1;
  while i < |items|
    invariant 1 <= i <= |items|
    invariant 0 <= minIdx < |items|
    decreases |items| - i
  {
    var val := StringToInt(items[i][0]);
    if val < minVal {
      minVal := val;
      minIdx := i;
    }
    i := i + 1;
  }
  item := items[minIdx][1];
  rest := items[0..minIdx] + items[minIdx + 1..];
}

// List all tasks in the queue
method ListAll(items: seq<seq<string>>) returns (tasks: seq<string>)
  requires forall i :: 0 <= i < |items| ==> |items[i]| >= 2
  ensures |tasks| == |items|
  decreases |items|
{
  tasks := [];
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    invariant |tasks| == i
    decreases |items| - i
  {
    tasks := tasks + [items[i][1]];
    i := i + 1;
  }
}

}