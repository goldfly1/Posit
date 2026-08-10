datatype Task = Task(id: int, name: string)
datatype Entry = Entry(task: Task, arrival: int, readyAt: int)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Enqueue(queue: seq<Entry>, entry: Entry) returns (newQueue: seq<Entry>)
  ensures |newQueue| == |queue| + 1
  ensures newQueue[|queue|] == entry
  ensures forall i :: 0 <= i < |queue| ==> newQueue[i] == queue[i]
{
  newQueue := queue + [entry];
}

function FindReady(queue: seq<Entry>, now: int): int
  decreases |queue|
  ensures FindReady(queue, now) == -1 || 0 <= FindReady(queue, now) < |queue|
  ensures FindReady(queue, now) >= 0 ==> queue[FindReady(queue, now)].readyAt <= now
  ensures FindReady(queue, now) == -1 ==> forall i :: 0 <= i < |queue| ==> queue[i].readyAt > now
{
  if |queue| == 0 then -1
  else if queue[0].readyAt <= now then 0
  else
    var rest := FindReady(queue[1..], now);
    if rest == -1 then -1 else rest + 1
}

method Dequeue(queue: seq<Entry>, now: int) returns (result: Result<Entry>, newQueue: seq<Entry>)
  requires |queue| > 0
  requires exists i :: 0 <= i < |queue| && queue[i].readyAt <= now
  ensures result.Success?
  ensures |newQueue| == |queue| - 1
  ensures result.value in queue
  ensures result.value.readyAt <= now
{
  var idx := FindReady(queue, now);
  result := Success(queue[idx]);
  newQueue := queue[..idx] + queue[idx + 1..];
}

method Peek(queue: seq<Entry>, now: int) returns (result: Result<Entry>)
  requires |queue| > 0
  requires exists i :: 0 <= i < |queue| && queue[i].readyAt <= now
  ensures result.Success?
  ensures result.value in queue
  ensures result.value.readyAt <= now
{
  var idx := FindReady(queue, now);
  result := Success(queue[idx]);
}