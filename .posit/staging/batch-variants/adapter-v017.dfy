datatype CsvRow = CsvRow(cells: seq<string>)
datatype ObjectNode = ObjField(name: string, value: string) | ObjNested(name: string, children: seq<ObjectNode>)

method AdaptCsvToObject(headers: seq<string>, row: CsvRow) returns (out: seq<ObjectNode>)
  requires |row.cells| == |headers|
  ensures |out| == |headers|
{
  out := [];
  var i := 0;
  while i < |headers|
    invariant 0 <= i <= |headers|
    invariant |out| == i
    decreases |headers| - i
  {
    out := out + [ObjField(headers[i], row.cells[i])];
    i := i + 1;
  }
}

method CountObjectFields(out: seq<ObjectNode>) returns (n: int)
  ensures n == |out|
  ensures n >= 0
{
  n := |out|;
}