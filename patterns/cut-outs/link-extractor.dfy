// Cut-out: link-extractor
// Pattern: extractor (conforms to parser pattern signatures)
// Domain: text processing
// Params: none (fully self-contained)
// responsibility: extract links (http URLs) from text
// test: ExtractLinks("visit http://example.com today") returns [["http://example.com","http://example.com"]]

// Extract links from text
// Finds URLs starting with "http" and extracts until whitespace
// Each row in result: [text, url]
method ExtractLinks(text: string) returns (links: seq<seq<string>>)
  requires |text| >= 0
  ensures |links| >= 0
  decreases |text|
{
  links := [];
  var i := 0;
  while i < |text|
    invariant 0 <= i <= |text|
    invariant |links| >= 0
    decreases |text| - i
  {
    // Check for "http" at position i
    if i + 3 < |text| && text[i] == 'h' && text[i+1] == 't' && text[i+2] == 't' && text[i+3] == 'p' {
      // Extract URL until whitespace or end
      var urlStart := i;
      var j := i;
      while j < |text| && text[j] != ' ' && text[j] != '\t' && text[j] != '\n' && text[j] != '\r'
        invariant 0 <= j <= |text|
        decreases |text| - j
      {
        j := j + 1;
      }
      var url := text[urlStart..j];
      links := links + [[url, url]];
      i := j;
    } else {
      i := i + 1;
    }
  }
}