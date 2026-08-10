datatype CsvRow = Row(cells: seq<string>)
datatype Obj = ObjData(name: string, value: string)

method Adapt(row: CsvRow) returns (obj: Obj)
  ensures obj.ObjData?
{
  if |row.cells| > 0 {
    obj := ObjData("col0", row.cells[0])
  } else {
    obj := ObjData("empty", "")
  }
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
    res := res + [Adapt(items[i])];
    i := i + 1;
  }
}

function IsEmpty(obj: Obj): bool { obj.name == "empty" }

method CountNonEmpty(items: seq<CsvRow>) returns (count: int)
  ensures count >= 0
  ensures count <= |items|
  decreases |items|
{
  count := 0;
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    invariant count <= i
    decreases |items| - i
  {
    var o := Adapt(items[i]);
    if !IsEmpty(o) { count := count + 1; }
    i := i + 1;
  }
}