// Cut-out: log-parser
// Pattern: parser (conforms to parser pattern signatures)
// Domain: log processing
// Params: none (fully self-contained)
// responsibility: parse log lines into timestamp, level, message
// test: ParseLogLine("2024-01-01 INFO message text") returns ("2024-01-01", "INFO", "message text")
// test: FilterByLevel(["2024-01-01 INFO hello", "2024-01-01 ERROR world"], "ERROR") returns ["2024-01-01 ERROR world"]
// test: CountByLevel(["2024-01-01 INFO hello", "2024-01-01 ERROR world", "2024-01-01 INFO foo"]) returns [["INFO","2"],["ERROR","1"]]


// Convert non-negative int to string
function IntToString(n: int): (s: string)
  requires n >= 0
  decreases n
{
  if n < 10 then ["0123456789"[n]]
  else IntToString(n / 10) + ["0123456789"[n % 10]]
}

// Parse a log line into timestamp, level, and message
// Format: "TIMESTAMP LEVEL MESSAGE" (space-separated)
method ParseLogLine(line: string) returns (timestamp: string, level: string, message: string)
  requires |line| >= 0
  decreases |line|
{
  timestamp := "";
  level := "";
  message := "";

  // Parse timestamp (until first space)
  var i := 0;
  while i < |line| && line[i] != ' '
    invariant 0 <= i <= |line|
    decreases |line| - i
  {
    timestamp := timestamp + [line[i]];
    i := i + 1;
  }
  // Skip space
  if i < |line| {
    i := i + 1;
  }
  // Parse level (until second space)
  while i < |line| && line[i] != ' '
    invariant 0 <= i <= |line|
    decreases |line| - i
  {
    level := level + [line[i]];
    i := i + 1;
  }
  // Skip space
  if i < |line| {
    i := i + 1;
  }
  // Rest is message
  message := line[i..];
}

// Filter log lines by level
method FilterByLevel(lines: seq<string>, level: string) returns (filtered: seq<string>)
  requires |lines| >= 0
  ensures |filtered| >= 0
  decreases |lines|
{
  filtered := [];
  var i := 0;
  while i < |lines|
    invariant 0 <= i <= |lines|
    invariant |filtered| >= 0
    decreases |lines| - i
  {
    var ts, lv, msg := ParseLogLine(lines[i]);
    if lv == level {
      filtered := filtered + [lines[i]];
    }
    i := i + 1;
  }
}

// Count log lines by level
// Each row in result: [level, count]
method CountByLevel(lines: seq<string>) returns (result: seq<seq<string>>)
  requires |lines| >= 0
  ensures |result| >= 0
  decreases |lines|
{
  var levels: seq<string> := [];
  var counts: seq<int> := [];
  var i := 0;
  while i < |lines|
    invariant 0 <= i <= |lines|
    invariant |levels| == |counts|
    invariant forall k :: 0 <= k < |counts| ==> counts[k] >= 0
    decreases |lines| - i
  {
    var ts, lv, msg := ParseLogLine(lines[i]);
    // Find level in levels
    var foundIdx := -1;
    var j := 0;
    while j < |levels|
      invariant 0 <= j <= |levels|
      invariant -1 <= foundIdx
      invariant foundIdx < |levels| || foundIdx == -1
      decreases |levels| - j
    {
      if levels[j] == lv {
        foundIdx := j;
      }
      j := j + 1;
    }
    if foundIdx >= 0 {
      counts := counts[foundIdx := counts[foundIdx] + 1];
    } else {
      levels := levels + [lv];
      counts := counts + [1];
    }
    i := i + 1;
  }

  // Build result rows [level, count]
  result := [];
  i := 0;
  while i < |levels|
    invariant 0 <= i <= |levels|
    invariant |result| == i
    invariant |levels| == |counts|
    invariant forall k :: 0 <= k < |counts| ==> counts[k] >= 0
    decreases |levels| - i
  {
    result := result + [[levels[i], IntToString(counts[i])]];
    i := i + 1;
  }
}

