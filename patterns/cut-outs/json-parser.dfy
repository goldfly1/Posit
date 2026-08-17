// Cut-out: json-parser
// Pattern: parser (conforms to parser pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)
// responsibility: parse a JSON array of objects into rows of string fields
// test: ParseJsonToArray("[{\"name\":\"Alice\",\"age\":\"30\"}]") returns [["name","age"],["Alice","30"]]

// Parse a JSON array of objects into rows of string fields.
// Input is a JSON string like: [{"name":"Alice","age":"30"},{"name":"Bob","age":"25"}]
// Output is rows: header row = keys from first object, data rows = values
method ParseJsonToArray(json: string) returns (rows: seq<seq<string>>)
  requires |json| >= 2
  ensures |rows| >= 1
  decreases |json|
{
  rows := [];
  // Find the opening bracket
  var i := 0;
  while i < |json| && json[i] != '['
    invariant 0 <= i <= |json|
    decreases |json| - i
  {
    i := i + 1;
  }
  if i >= |json| - 1 {
    rows := [[""]];
    return;
  }
  i := i + 1; // skip '['

  var headers: seq<string> := [];
  var first := true;

  // Parse each object in the array
  while i < |json| - 1
    invariant 0 <= i <= |json|
    invariant |rows| >= 0
    decreases |json| - i
  {
    // Skip whitespace and commas
    while i < |json| && (json[i] == ',' || json[i] == ' ' || json[i] == '\n' || json[i] == '\r' || json[i] == '\t')
      invariant 0 <= i <= |json|
      decreases |json| - i
    {
      i := i + 1;
    }
    if i >= |json| || json[i] == ']' {
      break;
    }
    if json[i] != '{' {
      i := i + 1;
      continue;
    }
    i := i + 1; // skip '{'

    var fields: seq<string> := [];
    var fieldNames: seq<string> := [];

    // Parse key-value pairs
    while i < |json| && json[i] != '}'
      invariant 0 <= i <= |json|
      decreases |json| - i
    {
      // Skip whitespace
      while i < |json| && (json[i] == ' ' || json[i] == ',' || json[i] == '\n')
        invariant 0 <= i <= |json|
        decreases |json| - i
      {
        i := i + 1;
      }
      if i >= |json| || json[i] == '}' {
        break;
      }

      // Parse key (quoted string)
      if json[i] == '"' {
        var keyStart := i + 1;
        i := i + 1;
        while i < |json| && json[i] != '"'
          invariant 0 <= i <= |json|
          decreases |json| - i
        {
          i := i + 1;
        }
        var key := json[keyStart..i];
        if i < |json| {
          i := i + 1; // skip closing quote
        }
        fieldNames := fieldNames + [key];

        // Skip colon and whitespace
        while i < |json| && (json[i] == ':' || json[i] == ' ')
          invariant 0 <= i <= |json|
          decreases |json| - i
        {
          i := i + 1;
        }

        // Parse value (quoted string)
        if i < |json| && json[i] == '"' {
          var valStart := i + 1;
          i := i + 1;
          while i < |json| && json[i] != '"'
            invariant 0 <= i <= |json|
            decreases |json| - i
          {
            i := i + 1;
          }
          var val := json[valStart..i];
          if i < |json| {
            i := i + 1; // skip closing quote
          }
          fields := fields + [val];
        } else {
          fields := fields + [""];
        }
      } else {
        i := i + 1;
      }
    }
    if i < |json| && json[i] == '}' {
      i := i + 1; // skip '}'
    }

    if first {
      headers := fieldNames;
      rows := rows + [headers];
      first := false;
    }
    rows := rows + [fields];
  }

  if |rows| == 0 {
    rows := [[""]];
  }
}