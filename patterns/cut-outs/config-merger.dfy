// Cut-out: config-merger
// Pattern: merger (conforms to transformer pattern signatures)
// Domain: config processing
// Params: none (fully self-contained)
// responsibility: merge two configs, config2 overrides config1
// test: MergeConfigs([["a","1"],["b","2"]], [["b","3"],["c","4"]]) returns [["a","1"],["b","3"],["c","4"]]

// Merge two configs (key-value pairs)
// config2 overrides config1 for matching keys
// New keys from config2 are appended
method MergeConfigs(config1: seq<seq<string>>, config2: seq<seq<string>>) returns (result: seq<seq<string>>)
  requires forall i :: 0 <= i < |config1| ==> |config1[i]| >= 2
  requires forall i :: 0 <= i < |config2| ==> |config2[i]| >= 2
  ensures |result| >= 0
  decreases |config2|
{
  result := config1;
  var i := 0;
  while i < |config2|
    invariant 0 <= i <= |config2|
    invariant |result| >= 0
    invariant forall k :: 0 <= k < |result| ==> |result[k]| >= 2
    decreases |config2| - i
  {
    var key := config2[i][0];
    var value := config2[i][1];

    // Find key in result
    var foundIdx := -1;
    var j := 0;
    while j < |result|
      invariant 0 <= j <= |result|
      invariant -1 <= foundIdx
      invariant foundIdx < |result| || foundIdx == -1
      decreases |result| - j
    {
      if |result[j]| >= 1 && result[j][0] == key {
        foundIdx := j;
      }
      j := j + 1;
    }

    if foundIdx >= 0 {
      // Override existing key
      result := result[foundIdx := [key, value]];
    } else {
      // Append new key-value
      result := result + [[key, value]];
    }
    i := i + 1;
  }
}