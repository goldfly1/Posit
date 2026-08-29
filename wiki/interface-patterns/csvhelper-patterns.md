# Interface Pattern: CsvHelper — Layered CSV Reading/Writing (Parsing → Reading → Mapping → Writing)

> **Source:** [CsvHelper](https://github.com/JoshClose/CsvHelper) by Josh Close — the most popular C# CSV library.
> Interfaces extracted from the `CsvHelper` namespace (latest version, src/CsvHelper).

## Problem Shape

Read a delimited text file → parse raw fields → map fields to typed objects → (optionally) write typed objects back as CSV.
Four distinct concerns: **tokenizing** (parser), **field-level access** (reader row), **object-level hydration** (reader), **serialization** (writer).
Mapping configuration is a cross-cutting concern that plugs into both reader and writer.

## Spec Verbs

parse, read, convert, map, write, validate, flush

## Component Interfaces

### 1. IParser — Low-level tokenizing (raw text → string[] fields)

```csharp
public interface IParser : IDisposable
{
    // State
    long ByteCount { get; }
    long CharCount { get; }
    int Count { get; }              // field count for current row
    string this[int index] { get; } // field at index (current row)
    string[]? Record { get; }       // all fields for current row
    string RawRecord { get; }       // raw unparsed row text
    int Row { get; }
    int RawRow { get; }
    string Delimiter { get; }

    // Shared state
    CsvContext Context { get; }
    IParserConfiguration Configuration { get; }

    // Navigation — returns bool (true = more data, false = EOF)
    bool Read();
    Task<bool> ReadAsync();
}
```

### 2. IReaderRow — Field-level access + type conversion (single row)

```csharp
public interface IReaderRow
{
    // State
    int ColumnCount { get; }
    int CurrentIndex { get; }
    string[]? HeaderRecord { get; }
    IParser Parser { get; }          // ← reader holds a parser reference
    CsvContext Context { get; }
    IReaderConfiguration Configuration { get; }

    // Raw field access by index or name
    string? this[int index] { get; }
    string? this[string name] { get; }
    string? this[string name, int index] { get; }
    string? GetField(int index);
    string? GetField(string name);
    string? GetField(string name, int index);

    // Typed field access (with conversion)
    object? GetField(Type type, int index);
    object? GetField(Type type, string name);
    T? GetField<T>(int index);
    T? GetField<T>(string name);
    T? GetField<T>(string name, int index);
    T? GetField<T>(int index, ITypeConverter converter);
    T? GetField<T, TConverter>(int index) where TConverter : ITypeConverter;
    T? GetField<T, TConverter>(string name) where TConverter : ITypeConverter;

    // TryGetField — bool + out pattern (no-throw field access)
    bool TryGetField(Type type, int index, out object? field);
    bool TryGetField(Type type, string name, out object? field);
    bool TryGetField<T>(int index, out T? field);
    bool TryGetField<T>(string name, out T? field);
    bool TryGetField<T>(int index, ITypeConverter converter, out T? field);
    bool TryGetField<T, TConverter>(int index, out T? field) where TConverter : ITypeConverter;

    // Object-level hydration (current row → typed record)
    T GetRecord<T>();
    T GetRecord<T>(T anonymousTypeDefinition);
    object GetRecord(Type type);
}
```

### 3. IReader — Record iteration + streaming (extends IReaderRow)

```csharp
public interface IReader : IReaderRow, IDisposable
{
    // Navigation
    bool ReadHeader();
    bool Read();
    Task<bool> ReadAsync();

    // Bulk: stream all records as typed objects
    IEnumerable<T> GetRecords<T>();
    IEnumerable<T> GetRecords<T>(T anonymousTypeDefinition);
    IEnumerable<object> GetRecords(Type type);

    // Bulk: hydrate into a reused instance
    IEnumerable<T> EnumerateRecords<T>(T record);

    // Async streaming
    IAsyncEnumerable<T> GetRecordsAsync<T>(CancellationToken ct = default);
    IAsyncEnumerable<T> GetRecordsAsync<T>(T anonymousTypeDefinition, CancellationToken ct = default);
    IAsyncEnumerable<object> GetRecordsAsync(Type type, CancellationToken ct = default);
    IAsyncEnumerable<T> EnumerateRecordsAsync<T>(T record, CancellationToken ct = default);
}
```

### 4. IWriterRow — Field-level writing (single row)

```csharp
public interface IWriterRow
{
    // State
    string?[]? HeaderRecord { get; }
    int Row { get; }
    int Index { get; }
    CsvContext Context { get; }
    IWriterConfiguration Configuration { get; }

    // Write a single field
    void WriteField(string? field);
    void WriteField(string? field, bool shouldQuote);
    void WriteField<T>(T? field);
    void WriteField<T>(T? field, ITypeConverter converter);
    void WriteField<T, TConverter>(T? field) where TConverter : ITypeConverter;
    void WriteConvertedField(string? field, Type fieldType);

    // Comments and headers
    void WriteComment(string? comment);
    void WriteHeader<T>();
    void WriteHeader(Type type);

    // Write a full record
    void WriteRecord<T>(T record);
}
```

### 5. IWriter — Record-level writing + flush (extends IWriterRow)

```csharp
public interface IWriter : IWriterRow, IDisposable, IAsyncDisposable
{
    // Row boundary
    void NextRecord();
    Task NextRecordAsync();

    // Bulk: write collections
    void WriteRecords(IEnumerable records);
    void WriteRecords<T>(IEnumerable<T> records);
    Task WriteRecordsAsync(IEnumerable records, CancellationToken ct = default);
    Task WriteRecordsAsync<T>(IEnumerable<T> records, CancellationToken ct = default);
    Task WriteRecordsAsync<T>(IAsyncEnumerable<T> records, CancellationToken ct = default);

    // Flush
    void Flush();
    Task FlushAsync();
}
```

### 6. ITypeConverter — String ↔ object conversion (used by both reader and writer)

```csharp
public interface ITypeConverter
{
    object? ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData);
    string? ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData);
}
```

### 7. ITypeConverterFactory — Pluggable converter creation

```csharp
public interface ITypeConverterFactory
{
    bool CanCreate(Type type);
    bool Create(Type type, TypeConverterCache cache, out ITypeConverter typeConverter);
}
```

### 8. IFactory — Component factory (creates parser → reader → writer chain)

```csharp
public interface IFactory
{
    IParser CreateParser(TextReader reader, CsvConfiguration configuration);
    IParser CreateParser(TextReader reader, CultureInfo cultureInfo);

    IReader CreateReader(TextReader reader, CsvConfiguration configuration);
    IReader CreateReader(TextReader reader, CultureInfo cultureInfo);
    IReader CreateReader(IParser parser);   // ← reader from an existing parser

    IWriter CreateWriter(TextWriter writer, CsvConfiguration configuration);
    IWriter CreateWriter(TextWriter writer, CultureInfo cultureInfo);

    IHasMap<T> CreateClassMapBuilder<T>();
}
```

### 9. IObjectResolver — Object instantiation abstraction (DI hook)

```csharp
public interface IObjectResolver
{
    bool UseFallback { get; }
    Func<Type, bool> CanResolve { get; }
    Func<Type, object[], object> ResolveFunction { get; }

    object Resolve(Type type, params object[] constructorArgs);
    T Resolve<T>(params object[] constructorArgs);
}
```

### 10. Mapping Configuration Interfaces (fluent builder)

```csharp
// Pluggable attribute-based config
public interface IClassMapper        { void ApplyTo(CsvConfiguration configuration); }
public interface IMemberMapper       { void ApplyTo(MemberMap memberMap); }
public interface IMemberReferenceMapper { void ApplyTo(MemberReferenceMap referenceMap); }
public interface IParameterMapper    { void ApplyTo(ParameterMap parameterMap); }
public interface IParameterReferenceMapper { void ApplyTo(ParameterReferenceMap referenceMap); }

// Fluent class map builder
public interface IHasMap<TClass> : IBuildableClass<TClass>
{
    IHasMapOptions<TClass, TMember> Map<TMember>(
        Expression<Func<TClass, TMember?>> expression, bool useExistingMap = true);
}

public interface IHasMapOptions<TClass, TMember> :
    IHasMap<TClass>, IHasTypeConverter<TClass, TMember>,
    IHasIndex<TClass, TMember>, IHasName<TClass, TMember>,
    IHasOptional<TClass, TMember>, IHasConvertUsing<TClass, TMember>,
    IHasDefault<TClass, TMember>, IHasConstant<TClass, TMember>,
    IHasValidate<TClass, TMember>
{ }

public interface IHasValidate<TClass, TMember> : IBuildableClass<TClass>
{
    // Returns IHasMap (for chaining). Validate delegate returns bool.
    IHasMap<TClass> Validate(Validate validateExpression);
}

public interface IBuildableClass<TClass>
{
    ClassMap<TClass> Build();
}
```

### 11. Configuration Interfaces (layered: parser → reader → writer)

```csharp
public interface IParserConfiguration
{
    CultureInfo CultureInfo { get; }
    bool CacheFields { get; }
    string NewLine { get; }
    bool IsNewLineSet { get; }
    CsvMode Mode { get; }
    int BufferSize { get; }
    int ProcessFieldBufferSize { get; }
    bool CountBytes { get; }
    Encoding Encoding { get; }
    BadDataFound? BadDataFound { get; }       // ← callback for bad data
    double MaxFieldSize { get; }
    bool LineBreakInQuotedFieldIsBadData { get; }
    char Comment { get; }
    bool AllowComments { get; }
    bool IgnoreBlankLines { get; }
    char Quote { get; }
    string Delimiter { get; }
    bool DetectDelimiter { get; }
    GetDelimiter GetDelimiter { get; }
    string[] DetectDelimiterValues { get; }
    char Escape { get; }
    TrimOptions TrimOptions { get; }
    char[] WhiteSpaceChars { get; }
    bool ExceptionMessagesContainRawData { get; }
    void Validate();                           // ← config validation method
}

public interface IReaderConfiguration : IParserConfiguration  // ← extends parser config
{
    bool HasHeaderRecord { get; }
    HeaderValidated? HeaderValidated { get; }         // ← callback for header validation
    MissingFieldFound? MissingFieldFound { get; }     // ← callback for missing fields
    ReadingExceptionOccurred? ReadingExceptionOccurred { get; } // ← callback for read exceptions
    PrepareHeaderForMatch PrepareHeaderForMatch { get; }
    ShouldUseConstructorParameters ShouldUseConstructorParameters { get; }
    GetConstructor GetConstructor { get; }
    GetDynamicPropertyName GetDynamicPropertyName { get; }
    bool IgnoreReferences { get; }
    ShouldSkipRecord? ShouldSkipRecord { get; }
    bool IncludePrivateMembers { get; }
    ReferenceHeaderPrefix? ReferenceHeaderPrefix { get; }
    bool DetectColumnCountChanges { get; }
    MemberTypes MemberTypes { get; }
}

public interface IWriterConfiguration
{
    int BufferSize { get; }
    CsvMode Mode { get; }
    string Delimiter { get; }
    char Quote { get; }
    char Escape { get; }
    TrimOptions TrimOptions { get; }
    InjectionOptions InjectionOptions { get; }
    char[] InjectionCharacters { get; }
    char InjectionEscapeCharacter { get; }
    string NewLine { get; }
    bool IsNewLineSet { get; }
    ShouldQuote ShouldQuote { get; }
    CultureInfo CultureInfo { get; }
    bool AllowComments { get; }
    char Comment { get; }
    bool HasHeaderRecord { get; }
    bool IgnoreReferences { get; }
    bool IncludePrivateMembers { get; }
    ReferenceHeaderPrefix? ReferenceHeaderPrefix { get; }
    MemberTypes MemberTypes { get; }
    bool UseNewObjectForNullReferenceMembers { get; }
    IComparer<string>? DynamicPropertySort { get; }
    bool ExceptionMessagesContainRawData { get; }
    void Validate();
}
```

## Type Chain

```
TextReader → IParser.Read() → string[]? Record → IReaderRow.GetField<T>() → T (typed field)
                                     ↓
                              IReader.GetRecords<T>() → IEnumerable<T> (typed records)
                                     ↓
                              IWriter.WriteRecords<T>(IEnumerable<T>) → TextWriter
```

Expanded with converter:

```
string? (raw field) → ITypeConverter.ConvertFromString() → object? (typed)
object? (typed)     → ITypeConverter.ConvertToString()  → string? (raw field)
```

Factory construction chain:

```
TextReader → IFactory.CreateParser() → IParser
IParser    → IFactory.CreateReader() → IReader  (reader wraps parser)
TextWriter → IFactory.CreateWriter() → IWriter
```

## Connection Order

```
TextReader
    ↓
IParser.Read()           // raw text → string[] fields (bool = more data)
    ↓
IReaderRow.GetField<T>() // string field → typed T (via ITypeConverter)
    ↓
IReader.GetRecords<T>()  // stream rows → IEnumerable<T> (uses ClassMap for mapping)
    ↓
IWriter.WriteRecords<T>() // IEnumerable<T> → string fields (via ITypeConverter)
    ↓
TextWriter
```

With mapping:

```
CsvContext.RegisterClassMap<TMap>() → ClassMap (member → index mapping)
    ↓ (used by reader)
IReader.GetRecords<T>()  // ClassMap tells reader which field index → which property
    ↓ (used by writer)
IWriter.WriteRecord<T>() // ClassMap tells writer which property → which field index
```

## Error Handling / Validation Patterns

CsvHelper uses **three distinct error strategies** depending on the concern:

### Strategy 1: Exception hierarchy (structural errors — always throws)

All exceptions derive from `CsvHelperException : Exception` and carry `CsvContext`:

```
CsvHelperException
├── ParserException              // parser-level errors
├── ReaderException              // reader-level errors
│   └── MissingFieldException    // header field not found
├── WriterException              // writer-level errors
├── BadDataException             // malformed CSV field (carries Field + RawRecord)
├── ConfigurationException       // bad config / empty map
├── TypeConverterException       // conversion failure (carries Text/Value + TypeConverter)
└── ValidationException (abstract)
    ├── HeaderValidationException  // header missing for mapped member (carries InvalidHeader[])
    └── FieldValidationException   // user Validate delegate returned false (carries Field)
```

**Key:** All exceptions include `CsvContext` (parser/reader state: row, column count, raw record).
`ExceptionMessagesContainRawData` config flag controls whether raw CSV data appears in messages.

### Strategy 2: Bool return + out parameter (field-level safe access)

```csharp
// No-throw field access: returns false instead of throwing on conversion failure
bool TryGetField<T>(int index, out T? field);
bool TryGetField<T>(string name, out T? field);
```

**Contrast:** `GetField<T>()` throws on failure; `TryGetField<T>()` returns bool + out.
This is the **only** place CsvHelper uses the bool/out pattern — at the field level, not the record level.

### Strategy 3: Delegate callbacks (configurable error policy)

Error handling is **injected via configuration delegates**, not hardcoded:

```csharp
// Bad data found — default throws BadDataException, user can replace with logging
public delegate void BadDataFound(BadDataFoundArgs args);

// Header validation — default throws HeaderValidationException, user can replace
public delegate void HeaderValidated(HeaderValidatedArgs args);

// Missing field — default throws MissingFieldException, user can replace
public delegate void MissingFieldFound(MissingFieldFoundArgs args);

// Reading exception — default re-throws, user can swallow; returns bool (true = re-throw)
public delegate bool ReadingExceptionOccurred(ReadingExceptionOccurredArgs args);

// Field validation — user-supplied; returns bool (true = valid, false = throw)
public delegate bool Validate(ValidateArgs args);
```

**Pattern:** The library provides a default throwing behavior, but every error point is a **delegate property on the configuration** that the caller can override. This lets callers choose between throw-on-error, log-and-continue, or custom handling — without changing the interface.

### Strategy 4: Config validation method

```csharp
// IParserConfiguration and IWriterConfiguration both expose:
void Validate();
```

Configuration objects self-validate via an explicit `Validate()` method (not called automatically; caller must invoke).

## Key Constraints

1. **Parser returns bool for navigation, throws for structural errors.** `IParser.Read()` returns `bool` (true = more data, false = EOF). Parsing failures (malformed fields) throw `ParserException` / `BadDataException`.

2. **Reader wraps Parser via composition, not inheritance.** `IReaderRow` exposes `IParser Parser { get; }` — the reader holds a parser reference. `IReader : IReaderRow, IDisposable` extends the row interface. The factory's `CreateReader(IParser parser)` constructor accepts a pre-built parser.

3. **Configuration is layered by interface inheritance.** `IReaderConfiguration : IParserConfiguration` — reader config extends parser config. This means the reader has access to all parser settings plus reader-specific ones. `IWriterConfiguration` is separate (does not extend parser config).

4. **Type conversion is a separate interface, not embedded.** `ITypeConverter` has exactly two methods: `ConvertFromString` (read) and `ConvertToString` (write). It receives `IReaderRow` / `IWriterRow` (not the full reader/writer) for context. This allows converters to be stateless and reusable.

5. **Mapping is a class, not an interface.** `ClassMap` is an abstract class (not an interface) with `MemberMaps`, `ReferenceMaps`, and `ParameterMaps` collections. The fluent builder uses interfaces (`IHasMap<T>`, `IHasMapOptions<T,TMember>`) for the builder API, but the final product is a concrete `ClassMap` registered on `CsvContext`.

6. **CsvContext is the shared state hub.** It holds `Parser`, `Reader`, `Writer` references, `ClassMapCollection Maps`, `TypeConverterCache`, and `Configuration`. Every interface exposes `CsvContext Context { get; }`. Exceptions carry it for diagnostic state.

7. **IDisposable on parser and reader; IDisposable + IAsyncDisposable on writer.** Resource cleanup is interface-level, not implementation-level.

8. **Error policy is injectable, not fixed.** The library never hardcodes "throw on error" — it provides default throwing delegates that callers can replace. `ReadingExceptionOccurred` even returns `bool` (true = re-throw, false = swallow), giving callers control over exception propagation.

9. **Validate delegate returns bool, but the framework throws on false.** The `Validate` delegate (`bool Validate(ValidateArgs)`) returns true/false. If false, the framework throws `FieldValidationException`. The bool is a validation result, not an error-handling mechanism — the throw is the error mechanism.

## Summary: Decomposition Lesson for Code Generators

CsvHelper decomposes CSV processing into **four layers** with clean boundaries:

| Layer | Interface | Input | Output | Error Style |
|-------|-----------|-------|--------|-------------|
| Parse | `IParser` | `TextReader` | `string[]?` (fields) | bool nav + throw on bad data |
| Read (row) | `IReaderRow` | `string` field + `Type` | `T?` (typed field) | throw OR `bool TryGetField` |
| Read (stream) | `IReader` | `IParser` + `ClassMap` | `IEnumerable<T>` (records) | throw (configurable via delegate) |
| Write | `IWriter` | `T` (record) + `ClassMap` | `TextWriter` | throw (configurable via delegate) |

Cross-cutting:

| Concern | Interface | Key Method |
|---------|-----------|------------|
| Type conversion | `ITypeConverter` | `ConvertFromString` / `ConvertToString` |
| Object creation | `IObjectResolver` | `Resolve` |
| Component creation | `IFactory` | `CreateParser` / `CreateReader` / `CreateWriter` |
| Mapping config | `ClassMap` (class) + `IHasMap<T>` (builder) | `Map<TMember>()` |
| Shared state | `CsvContext` | `RegisterClassMap<T>()` / `AutoMap<T>()` |

**The key insight:** each layer wraps the one below it (parser → reader-row → reader) and adds a concern (tokenize → field access → record hydration). The type converter is a strategy injected at the field level. Mapping is a configuration concern registered on shared context, not a pipeline stage. Error handling is a delegate property, not a return type.