// Cut-out: price-converter
// Pattern: converter (conforms to transformer pattern signatures)
// Domain: finance
// Params: none (fully self-contained)
// responsibility: convert prices using exchange rates (integer math)
// test: ConvertPrice(1000, 250) returns 2500

// Convert price using rate (price * rate / 100, integer math)
method ConvertPrice(price: int, rate: int) returns (result: int)
  requires price >= 0
  requires rate >= 0
{
  result := price * rate / 100;
}