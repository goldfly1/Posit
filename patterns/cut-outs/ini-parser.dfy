// Cut-out: ini-parser
// Pattern: parser (conforms to parser pattern signatures)
// Domain: config processing
// Params: none (fully self-contained)
// responsibility: parse INI content into key-value pairs
// test: ParseIni("name=Alice\nage=30") returns [["name","Alice"],["age","30"]]

// Parse INI content into key-value pairs
// Skips section headers ([section]) and comments (; or #)
// Each row in result: [key, value]
method ParseIni(content: string) returns (result: seq<seq<string>>)
  requires |content| >= 0
  ensures |result| >= 0
  decreases |content|
{
  result := [];
  var i := 0;
  while i < |content|
    invariant 0 <= i <= |content|
    invariant |result| >= 0
    decreases |content| - i
  {
    // Skip whitespace and newlines
    while i < |content| && (content[i] == ' ' || content[i] == '\n' || content[i] == '\r' || content[i] == '\t')
      invariant 0 <= i <= |content|
      decreases |content| - i
    {
      i := i + 1;
    }
    if i >= |content| {
      break;
    }

    if content[i] == '[' {
      // Skip section headers
      while i < |content| && content[i] != '\n'
        invariant 0 <= i <= |content|
        decreases |content| - i
      {
        i := i + 1;
      }
    } else if content[i] == ';' || content[i] == '#' {
      // Skip comment lines
      while i < |content| && content[i] != '\n'
        invariant 0 <= i <= |content|
        decreases |content| - i
      {
        i := i + 1;
      }
    } else {
      // Parse key=value
      var key := "";
      while i < |content| && content[i] != '=' && content[i] != '\n'
        invariant 0 <= i <= |content|
        decreases |content| - i
      {
        key := key + [content[i]];
        i := i + 1;
      }
      // Skip '='
      if i < |content| && content[i] == '=' {
        i := i + 1;
      }
      // Parse value (until newline or end)
      var value := "";
      while i < |content| && content[i] != '\n' && content[i] != '\r'
        invariant 0 <= i <= |content|
        decreases |content| - i
      {
        if content[i] != ' ' || |value| > 0 {
          value := value + [content[i]];
        }
        i := i + 1;
      }
      result := result + [[key, value]];
    }
  }
}