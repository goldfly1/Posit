// Cut-out: word-tokenizer
// Pattern: tokenizer (conforms to parser pattern signatures)
// Domain: text processing
// Params: none (fully self-contained)
// responsibility: tokenize text into words by whitespace
// test: Tokenize("hello world foo") returns ["hello","world","foo"]
// test: CountWords("hello world foo") returns 3

// Tokenize text into words by splitting on whitespace
method Tokenize(text: string) returns (tokens: seq<string>)
  requires |text| >= 0
  ensures |tokens| >= 0
  decreases |text|
{
  tokens := [];
  var current := "";
  var i := 0;
  while i < |text|
    invariant 0 <= i <= |text|
    invariant |tokens| >= 0
    decreases |text| - i
  {
    if text[i] == ' ' || text[i] == '\t' || text[i] == '\n' || text[i] == '\r' {
      if |current| > 0 {
        tokens := tokens + [current];
        current := "";
      }
    } else {
      current := current + [text[i]];
    }
    i := i + 1;
  }
  if |current| > 0 {
    tokens := tokens + [current];
  }
}

// Count words in text (words are whitespace-separated)
method CountWords(text: string) returns (count: int)
  requires |text| >= 0
  ensures count >= 0
  decreases |text|
{
  var tokens := Tokenize(text);
  count := |tokens|;
}