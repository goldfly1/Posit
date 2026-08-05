---
title: "Dafny Contract Templates"
type: pattern
tags: [dafny, contracts, templates, architect]
component: dafny
version: 1.0.0
last_updated: 2026-08-06
---

# Dafny Contract Templates

Templates the architect uses when writing .dfy skeletons. Each template shows the contract pattern for a common module type. The architect picks the closest template, fills in the types and methods, and writes requires/ensures clauses. Imp fills in the bodies.

## Parser Module

```dafny
module CsvParser_Module {

  datatype ParseError = ParseError(line: int, col: int, message: string)

  class CsvParser {
    var delimiter: char
    var quote: char

    predicate Valid() reads this
      { delimiter != '\000' }

    constructor(delimiter: char, quote: char)
      ensures Valid()
    { }

    method ParseLine(line: string) returns (fields: seq<string>, error: ParseError?)
      requires Valid()
      requires |line| > 0
      ensures |fields| >= 0
      ensures error == null ==> |fields| >= 1
    { }
  }
}
```

## Validator Module

```dafny
module Validator_Module {

  datatype DataType = Integer | Float | Date | Boolean | Varchar
  datatype ValidationResult = Valid | Invalid(reason: string)

  class DataValidator {
    method Validate(value: string, type: DataType) returns (result: ValidationResult)
      requires |value| >= 0
      ensures result.Valid? || result.Invalid?
    { }
  }
}
```

## Generator Module

```dafny
module SqlGenerator_Module {

  datatype DatabaseDialect = PostgreSql | Sqlite | MySql

  class SqlInsertBuilder {
    var dialect: DatabaseDialect
    var tableName: string

    predicate Valid() reads this
      { |tableName| > 0 }

    constructor(dialect: DatabaseDialect, tableName: string)
      requires |tableName| > 0
      ensures Valid()
    { }

    method BuildInsert(columns: seq<string>, values: seq<string>) returns (sql: string)
      requires Valid()
      requires |columns| == |values|
      requires |columns| > 0
      ensures |sql| > 0
    { }
  }
}
```

## Config/Schema Module

```dafny
module SchemaMapper_Module {

  datatype ColumnSchema = ColumnSchema(
    name: string,
    dataType: string,
    nullable: bool,
    maxLength: int
  )

  class SchemaValidator {
    method ValidateSchema(columns: seq<ColumnSchema>, header: seq<string>) returns (valid: bool, errors: seq<string>)
      requires |columns| > 0
      requires |header| > 0
      ensures valid ==> |errors| == 0
    { }
  }
}
```

## I/O Shell Module (NOT Dafny — C# only)

```csharp
// io-shell module — NOT verified, just compiles
// This module does NOT get a .dfy file
public class FileService
{
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
}
```

## Rules for the Architect

1. Pure logic modules get Dafny contracts. I/O modules get C# type shells.
2. Every `method` gets at least one `requires` and one `ensures`.
3. Every `class` with mutable state gets a `predicate Valid()`.
4. Constructors must `ensures Valid()`.
5. Mutating methods must `requires Valid()` and `ensures Valid()` and `modifies this`.
6. Keep it minimal — types and signatures only. No bodies (just `{ }`).
7. Max 200 lines, 10 methods, 5 classes per module.
8. Use `datatype` for enums and records. Use `class` for stateful types.
9. Use `string` for text (not `seq<char>` — Dafny maps it automatically).
10. Use `int` for numbers (not `BigInteger` — Dafny's `int` is arbitrary precision).