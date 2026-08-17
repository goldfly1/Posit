// Cut-out: temperature-converter
// Pattern: converter (conforms to transformer pattern signatures)
// Domain: utility
// Params: none (fully self-contained)
// responsibility: convert temperatures between C, F, and K
// test: Convert(100, "C") returns (100, "C")
// test: Convert(212, "F") returns (100, "C")
// test: Convert(373, "K") returns (100, "C")

// Convert temperature from given unit to Celsius
method Convert(temp: int, unit: string) returns (result: int, outUnit: string)
  requires |unit| >= 0
  ensures |outUnit| >= 1
  decreases |unit|
{
  outUnit := "C";
  if |unit| >= 1 && unit[0] == 'F' {
    result := (temp - 32) * 5 / 9;
  } else if |unit| >= 1 && unit[0] == 'K' {
    result := temp - 273;
  } else {
    result := temp;
  }
}