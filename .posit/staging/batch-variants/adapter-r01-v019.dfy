datatype CsvRow = Row(cells: seq<string>)
datatype Obj = ObjData(name: string, value: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Validate(row: CsvRow) returns (result: Result<CsvRow>)
  ensures result.Success? ==> result.value == row
  ensures result.Failure? ==> |result.error| > 0
{
  if |row.cells| == 0 {
    result := Failure("no cells")
  } else if |row.cells[0]| == 0 {
    result := Failure("empty first cell")
  } else {
    result := Success(row)
  }
}

method Adapt(row: CsvRow) returns (obj: Obj)
  requires |row.cells| > 0
  requires |row.cells[0]| > 0
  ensures obj.ObjData?
{
  obj := ObjData("col0", row.cells[0])
}

method BatchAdapt(items: seq<CsvRow>) returns (res: seq<Obj>)
  requires |items| > 0
  ensures |res| == |items|
  decreases |items|
{
  res := [];
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    invariant |res| == i
    decreases |items| - i
  {
    var v := Validate(items[i]);
    if v.Success? {
      res := res + [Adapt(v.value)];
    } else {
      res := res + [ObjData("err", "")];
    }
    i := i + 1;
  }
}