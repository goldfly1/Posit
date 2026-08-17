// Cut-out: csv-serializer
// Pattern: transformer (conforms to transformer pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)
// responsibility: serialize rows of string fields into CSV format
// test: SerializeToCsv([["name","age"],["Alice","30"],["Bob","25"]]) returns "name,age\nAlice,30\nBob,25"

// Serialize rows to CSV string. First row is header.
// Each row's fields are joined by comma. Rows joined by newline.
method SerializeToCsv(rows: seq<seq<string>>) returns (csv: string)
  requires |rows| >= 1
  ensures |csv| >= 0
  decreases |rows|
{
  var sb := "";
  var i := 0;
  while i < |rows|
    invariant 0 <= i <= |rows|
    invariant |sb| >= 0
    decreases |rows| - i
  {
    if i > 0 {
      sb := sb + "\n";
    }
    var row := rows[i];
    var j := 0;
    while j < |row|
      invariant 0 <= j <= |row|
      decreases |row| - j
    {
      if j > 0 {
        sb := sb + ",";
      }
      sb := sb + row[j];
      j := j + 1;
    }
    i := i + 1;
  }
  csv := sb;
}