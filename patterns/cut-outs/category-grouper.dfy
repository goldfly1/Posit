// Cut-out: category-grouper
// Pattern: grouper (conforms to aggregator pattern signatures)
// Domain: data processing
// Params: categoryIndex (column index for grouping)
// responsibility: group rows by category and count
// test: GroupByCategory([["fruit","apple"],["fruit","banana"],["veg","carrot"]], 0) returns [["fruit","2"],["veg","1"]]

module CategoryGrouper {

// Convert non-negative int to string
function IntToString(n: int): (s: string)
  requires n >= 0
  decreases n
{
  if n < 10 then ["0123456789"[n]]
  else IntToString(n / 10) + ["0123456789"[n % 10]]
}

// Group rows by category at given column index
// Each row in result: [category, count]
method GroupByCategory(rows: seq<seq<string>>, categoryIndex: int) returns (result: seq<seq<string>>)
  requires categoryIndex >= 0
  requires forall i :: 0 <= i < |rows| ==> |rows[i]| > categoryIndex
  ensures |result| >= 0
  decreases |rows|
{
  var categories: seq<string> := [];
  var counts: seq<int> := [];
  var i := 0;
  while i < |rows|
    invariant 0 <= i <= |rows|
    invariant |categories| == |counts|
    invariant forall k :: 0 <= k < |counts| ==> counts[k] >= 0
    decreases |rows| - i
  {
    var cat := rows[i][categoryIndex];
    // Find category in categories
    var foundIdx := -1;
    var j := 0;
    while j < |categories|
      invariant 0 <= j <= |categories|
      invariant -1 <= foundIdx
      invariant foundIdx < |categories| || foundIdx == -1
      decreases |categories| - j
    {
      if categories[j] == cat {
        foundIdx := j;
      }
      j := j + 1;
    }
    if foundIdx >= 0 {
      counts := counts[foundIdx := counts[foundIdx] + 1];
    } else {
      categories := categories + [cat];
      counts := counts + [1];
    }
    i := i + 1;
  }

  // Build result rows [category, count]
  result := [];
  i := 0;
  while i < |categories|
    invariant 0 <= i <= |categories|
    invariant |result| == i
    invariant |categories| == |counts|
    invariant forall k :: 0 <= k < |counts| ==> counts[k] >= 0
    decreases |categories| - i
  {
    result := result + [[categories[i], IntToString(counts[i])]];
    i := i + 1;
  }
}

}