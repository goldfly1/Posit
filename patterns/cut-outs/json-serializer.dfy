// Cut-out: json-serializer
// Pattern: transformer (conforms to transformer pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)
// responsibility: convert rows of string fields into a JSON array string
// test: SerializeToJson([["name","age"],["Alice","30"]]) returns '[{"name":"Alice","age":"30"}]'

// Serialize rows to JSON array. First row is header (field names).
// Remaining rows are data objects.
method SerializeToJson(rows: seq<seq<string>>) returns (json: string)
  requires |rows| >= 1
  ensures |json| >= 2
  decreases |rows|
{
  if |rows| == 1 {
    json := "[]";
    return;
  }
  
  var header := rows[0];
  var sb := "[";
  var i := 1;
  while i < |rows|
    invariant 0 <= i <= |rows|
    invariant |sb| >= 1
    decreases |rows| - i
  {
    if i > 1 {
      sb := sb + ",";
    }
    sb := sb + "{";
    var row := rows[i];
    var j := 0;
    while j < |row| && j < |header|
      invariant 0 <= j <= |row|
      invariant 0 <= j <= |header|
      decreases |row| - j
    {
      if j > 0 {
        sb := sb + ",";
      }
      sb := sb + "\"" + header[j] + "\":" + "\"" + row[j] + "\"";
      j := j + 1;
    }
    sb := sb + "}";
    i := i + 1;
  }
  sb := sb + "]";
  json := sb;
}

// Serialize a single row to a JSON object string
method SerializeRow(header: seq<string>, row: seq<string>) returns (json: string)
  requires |header| >= 0
  requires |row| >= 0
  decreases |row|
{
  json := "{";
  var i := 0;
  while i < |row| && i < |header|
    invariant 0 <= i <= |row|
    invariant 0 <= i <= |header|
    decreases |row| - i
  {
    if i > 0 {
      json := json + ",";
    }
    json := json + "\"" + header[i] + "\":" + "\"" + row[i] + "\"";
    i := i + 1;
  }
  json := json + "}";
}