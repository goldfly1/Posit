// Cut-out: frequency-aggregator
// Pattern: aggregator (conforms to aggregator pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)
// responsibility: count word frequencies and sort by count descending
// test: CountFrequency(["a","b","a","c","a","b"]) returns [["3","a"],["2","b"],["1","c"]]


// Convert non-negative int to string
function IntToString(n: int): (s: string)
  requires n >= 0
  decreases n
{
  if n < 10 then ["0123456789"[n]]
  else IntToString(n / 10) + ["0123456789"[n % 10]]
}

// Count frequency of each word, sorted by count descending
// Each row in result: [count, word]
method CountFrequency(words: seq<string>) returns (result: seq<seq<string>>)
  requires |words| >= 0
  ensures |result| >= 0
  decreases |words|
{
  // Collect unique words
  var unique: seq<string> := [];
  var i := 0;
  while i < |words|
    invariant 0 <= i <= |words|
    invariant |unique| >= 0
    decreases |words| - i
  {
    var found := false;
    var j := 0;
    while j < |unique|
      invariant 0 <= j <= |unique|
      decreases |unique| - j
    {
      if words[i] == unique[j] {
        found := true;
      }
      j := j + 1;
    }
    if !found {
      unique := unique + [words[i]];
    }
    i := i + 1;
  }

  // Count each unique word
  var counts: seq<int> := [];
  i := 0;
  while i < |unique|
    invariant 0 <= i <= |unique|
    invariant |counts| == i
    invariant forall k :: 0 <= k < |counts| ==> counts[k] >= 0
    decreases |unique| - i
  {
    var count := 0;
    var j := 0;
    while j < |words|
      invariant 0 <= j <= |words|
      decreases |words| - j
    {
      if unique[i] == words[j] {
        count := count + 1;
      }
      j := j + 1;
    }
    counts := counts + [count];
    i := i + 1;
  }

  // Selection sort by count descending
  i := 0;
  while i < |counts|
    invariant 0 <= i <= |counts|
    invariant |counts| == |unique|
    invariant forall k :: 0 <= k < |counts| ==> counts[k] >= 0
    decreases |counts| - i
  {
    var maxIdx := i;
    var j := i + 1;
    while j < |counts|
      invariant i + 1 <= j <= |counts|
      invariant 0 <= maxIdx < |counts|
      decreases |counts| - j
    {
      if counts[j] > counts[maxIdx] {
        maxIdx := j;
      }
      j := j + 1;
    }
    // Swap counts[i] and counts[maxIdx]
    var tmpCount := counts[i];
    counts := counts[i := counts[maxIdx]];
    counts := counts[maxIdx := tmpCount];
    // Swap unique[i] and unique[maxIdx]
    var tmpWord := unique[i];
    unique := unique[i := unique[maxIdx]];
    unique := unique[maxIdx := tmpWord];
    i := i + 1;
  }

  // Build result rows [count, word]
  result := [];
  i := 0;
  while i < |unique|
    invariant 0 <= i <= |unique|
    invariant |result| == i
    invariant |counts| == |unique|
    invariant forall k :: 0 <= k < |counts| ==> counts[k] >= 0
    decreases |unique| - i
  {
    result := result + [[IntToString(counts[i]), unique[i]]];
    i := i + 1;
  }
}

