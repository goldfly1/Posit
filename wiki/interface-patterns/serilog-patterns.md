# Interface Pattern: Enrich → Filter → Format → Sink Pipeline (Serilog)

## Problem Shape
Accept structured log events → enrich with contextual properties → filter by level or custom criteria → format into text → emit to one or more sinks (console, file, network).
A multi-stage pipeline where each stage transforms the same core data type (LogEvent) and passes it forward.
Levels provide a fast early-exit filter before any work is done; custom filters run after enrichment.

## Spec Verbs
log, enrich, filter, format, emit, sink, destructur

## Source
[Serilog](https://github.com/serilog/serilog) — the most popular structured logging library for C#/.NET.
All signatures below are extracted from the actual source code (commit at time of cloning).

---

## Component Interfaces

### 1. ILogger — the public-facing API (entry point)

```csharp
namespace Serilog;

public interface ILogger
{
    // Contextual sub-loggers — decorator pattern, returns a new ILogger with enricher attached
    ILogger ForContext(ILogEventEnricher enricher);
    ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers);
    ILogger ForContext(string propertyName, object? value, bool destructureObjects = false);
    ILogger ForContext<TSource>();
    ILogger ForContext(Type source);

    // Write a pre-built event
    void Write(LogEvent logEvent);

    // Write with level + message template + property values (overloads for 0-3 typed args + params array)
    void Write(LogEventLevel level, string messageTemplate);
    void Write<T>(LogEventLevel level, string messageTemplate, T propertyValue);
    void Write<T0, T1>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1);
    void Write<T0, T1, T2>(LogEventLevel level, string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2);
    void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues);

    // Write with level + exception + message template
    void Write(LogEventLevel level, Exception? exception, string messageTemplate);
    void Write<T>(LogEventLevel level, Exception? exception, string messageTemplate, T propertyValue);
    void Write<T0, T1>(LogEventLevel level, Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1);
    void Write<T0, T1, T2>(LogEventLevel level, Exception? exception, string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2);
    void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues);

    // Level check — fast early-exit gate
    bool IsEnabled(LogEventLevel level);

    // Convenience methods per level (Verbose, Debug, Information, Warning, Error, Fatal)
    // Each has the same overload set as Write (without the level parameter).
    void Verbose(string messageTemplate);
    void Verbose<T>(string messageTemplate, T propertyValue);
    // ... same pattern for Debug, Information, Warning, Error, Fatal
}
```

### 2. ILogEventEnricher — adds/modifies properties on the event

```csharp
namespace Serilog.Core;

public interface ILogEventEnricher
{
    // Mutates logEvent in-place: adds, updates, or removes properties via the property factory
    void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory);
}
```

### 3. ILogEventFilter — custom filtering after enrichment, before sink

```csharp
namespace Serilog.Core;

public interface ILogEventFilter
{
    // Returns true if the event should pass through; false to drop it
    bool IsEnabled(LogEvent logEvent);
}
```

### 4. ILogEventSink — the terminal destination (console, file, network, etc.)

```csharp
namespace Serilog.Core;

public interface ILogEventSink
{
    // Emit the provided log event to the sink. Exceptions propagate to the caller.
    void Emit(LogEvent logEvent);
}
```

### 5. IBatchedLogEventSink — async batch variant for high-throughput sinks

```csharp
namespace Serilog.Core;

public interface IBatchedLogEventSink
{
    Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch);
    Task OnEmptyBatchAsync();
}
```

### 6. ITextFormatter — renders a LogEvent into text output

```csharp
namespace Serilog.Formatting;

public interface ITextFormatter
{
    // Format the log event into the output TextWriter
    void Format(LogEvent logEvent, TextWriter output);
}
```

### 7. ILogEventPropertyFactory — creates properties from .NET objects

```csharp
namespace Serilog.Core;

public interface ILogEventPropertyFactory
{
    LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false);
}
```

### 8. ILogEventPropertyValueFactory — converts .NET objects to structured values

```csharp
namespace Serilog.Core;

public interface ILogEventPropertyValueFactory
{
    LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false);
}
```

### 9. IDestructuringPolicy — pluggable policy for converting complex objects

```csharp
namespace Serilog.Core;

public interface IDestructuringPolicy
{
    bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue? result);
}
```

### 10. ILoggerSettings — external configuration source

```csharp
namespace Serilog.Configuration;

public interface ILoggerSettings
{
    void Configure(LoggerConfiguration loggerConfiguration);
}
```

---

## Core Data Types (the type chain)

### LogEventLevel — the enum that gates the pipeline

```csharp
namespace Serilog.Events;

public enum LogEventLevel
{
    Verbose,       // 0 — everything
    Debug,         // 1 — internal system events
    Information,   // 2 — operational intelligence (default minimum)
    Warning,       // 3 — service degraded
    Error,         // 4 — functionality unavailable
    Fatal          // 5 — pager goes off
}
```

### LogEvent — the central data object that flows through the entire pipeline

```csharp
namespace Serilog.Events;

public class LogEvent
{
    public DateTimeOffset Timestamp { get; }
    public LogEventLevel Level { get; }
    public MessageTemplate MessageTemplate { get; }
    public IReadOnlyDictionary<string, LogEventPropertyValue> Properties { get; }
    public Exception? Exception { get; }
    public ActivityTraceId? TraceId { get; }
    public ActivitySpanId? SpanId { get; }

    // Mutators used by enrichers
    public void AddOrUpdateProperty(LogEventProperty property);
    public void AddPropertyIfAbsent(LogEventProperty property);
    public void AddPropertyIfAbsent(ILogEventPropertyFactory factory, string name, object? value, bool destructureObjects = false);
    public void RemovePropertyIfPresent(string propertyName);

    // Rendering
    public void RenderMessage(TextWriter output, IFormatProvider? formatProvider = null);
    public string RenderMessage(IFormatProvider? formatProvider = null);
}
```

### LogEventProperty — a named property on the event

```csharp
namespace Serilog.Events;

public class LogEventProperty
{
    public string Name { get; }
    public LogEventPropertyValue Value { get; }

    public LogEventProperty(string name, LogEventPropertyValue value);
    public static bool IsValidName(string? name);
}
```

### LogEventPropertyValue — abstract base for structured property values

```csharp
namespace Serilog.Events;

public abstract class LogEventPropertyValue : IFormattable
{
    public abstract void Render(TextWriter output, string? format = null, IFormatProvider? formatProvider = null);
}
// Subtypes: ScalarValue, SequenceValue, StructureValue, DictionaryValue
```

### LoggingLevelSwitch — runtime-adjustable level gate

```csharp
namespace Serilog.Core;

public class LoggingLevelSwitch
{
    public LoggingLevelSwitch(LogEventLevel initialMinimumLevel = LogEventLevel.Information);
    public LogEventLevel MinimumLevel { get; set; }
    public event EventHandler<LoggingLevelSwitchChangedEventArgs>? MinimumLevelChanged;
}
```

---

## Type Chain

```
string (messageTemplate) + object?[] (propertyValues) + LogEventLevel + Exception?
    → MessageTemplateProcessor.Process()
    → MessageTemplate (parsed) + EventProperty[] (bound properties)
    → new LogEvent(timestamp, level, exception, messageTemplate, properties)
    → ILogEventEnricher.Enrich(logEvent, propertyFactory)        // mutates logEvent.Properties
    → ILogEventFilter.IsEnabled(logEvent) × N filters            // short-circuit: false → drop
    → ILogEventSink.Emit(logEvent)                               // fan-out to N sinks
        → ITextFormatter.Format(logEvent, TextWriter)           // inside each sink
        → TextWriter (output stream: console, file, network)
```

---

## Connection Order (the actual pipeline in Logger.PostLevelCheckEmit)

```
User calls ILogger.Write(level, messageTemplate, propertyValues)
  │
  ├─① Level Check (fast early-exit)
  │   Logger.IsEnabled(level)
  │   → if level < _minimumLevel: return (no allocation, no work)
  │   → if _levelSwitch != null && level < _levelSwitch.MinimumLevel: return
  │
  ├─② Message Template Processing
  │   MessageTemplateProcessor.Process(template, values, out parsedTemplate, out boundProperties)
  │   → parses template string into MessageTemplate
  │   → binds property values → EventProperty[] via PropertyValueConverter
  │
  ├─③ LogEvent Construction
  │   new LogEvent(timestamp, level, exception, parsedTemplate, boundProperties)
  │
  └─④ PostLevelCheckEmit(logEvent)
      │
      ├─⑤ Enrichment (mutate in-place)
      │   _enricher.Enrich(logEvent, _messageTemplateProcessor)
      │   → SafeAggregateEnricher iterates ILogEventEnricher[] with try/catch per enricher
      │   → each enricher calls logEvent.AddOrUpdateProperty() or AddPropertyIfAbsent()
      │
      └─⑥ Sink Emission
          _sink.Emit(logEvent)
          → if filters configured: FilteringSink wraps the aggregate sink
              FilteringSink.Emit(logEvent)
                → foreach ILogEventFilter: if !filter.IsEnabled(logEvent) → return (drop)
                → _sink.Emit(logEvent)  (delegate to inner sink)
          → SafeAggregateSink.Emit(logEvent)
                → foreach ILogEventSink: sink.Emit(logEvent) with try/catch per sink
                → each sink internally calls ITextFormatter.Format(logEvent, output)
```

---

## LoggerConfiguration — how the pipeline is assembled

```csharp
namespace Serilog;

public class LoggerConfiguration
{
    // Collections assembled via fluent configuration
    List<ILogEventSink> _logEventSinks;
    List<ILogEventSink> _auditSinks;
    List<ILogEventEnricher> _enrichers;
    List<ILogEventFilter> _filters;
    List<IDestructuringPolicy> _additionalDestructuringPolicies;
    Dictionary<string, LoggingLevelSwitch> _overrides;
    LogEventLevel _minimumLevel = LogEventLevel.Information;
    LoggingLevelSwitch? _levelSwitch;

    // Fluent configuration entry points
    LoggerSinkConfiguration WriteTo { get; }          // .WriteTo.Console(), .WriteTo.File(path)
    LoggerAuditSinkConfiguration AuditTo { get; }     // .AuditTo.Sink(sink) — exceptions propagate
    LoggerMinimumLevelConfiguration MinimumLevel { get; }  // .MinimumLevel.Debug()
    LoggerEnrichmentConfiguration Enrich { get; }     // .Enrich.WithProperty("X", 1)
    LoggerFilterConfiguration Filter { get; }         // .Filter.With(filter)
    LoggerDestructuringConfiguration Destructure { get; }  // .Destructure.With(policy)
    LoggerSettingsConfiguration ReadFrom { get; }     // .ReadFrom.AppSettings()

    Logger CreateLogger()
    {
        // 1. Aggregate all sinks into one SafeAggregateSink (fan-out with per-sink error isolation)
        ILogEventSink sink = new SafeAggregateSink(_logEventSinks);

        // 2. Wrap with FilteringSink if any filters configured (filter runs before sink emit)
        if (_filters.Any())
            sink = new FilteringSink(sink, _filters, auditing);

        // 3. Create the property converter + message template processor
        var converter = new PropertyValueConverter(...);
        var processor = new MessageTemplateProcessor(converter);

        // 4. Aggregate enrichers into SafeAggregateEnricher (per-enricher error isolation)
        var enricher = _enrichers.Count switch {
            0 => new EmptyEnricher(),
            1 => _enrichers[0],
            _ => new SafeAggregateEnricher(_enrichers)
        };

        // 5. Construct the Logger with all parts wired
        return new Logger(processor, _minimumLevel, _levelSwitch, sink, enricher, Dispose, overrideMap);
    }
}
```

### Logger — the concrete pipeline implementation

```csharp
namespace Serilog.Core;

// Logger implements ILogger (public API), ILogEventSink (can be used as a sub-logger sink),
// and IDisposable (flushes buffered sinks)
public sealed class Logger : ILogger, ILogEventSink, IDisposable
{
    readonly MessageTemplateProcessor _messageTemplateProcessor;  // parses templates, creates properties
    readonly ILogEventSink _sink;                                // terminal sink (may be FilteringSink → SafeAggregateSink)
    readonly ILogEventEnricher _enricher;                        // enricher chain (may be SafeAggregateEnricher)
    readonly LogEventLevel _minimumLevel;                        // fast level gate (CPU-cacheable field)
    readonly LoggingLevelSwitch? _levelSwitch;                   // dynamic level gate
    readonly LevelOverrideMap? _overrideMap;                     // per-source-context level overrides

    // The actual pipeline execution:
    void PostLevelCheckEmit(LogEvent logEvent)
    {
        try { _enricher.Enrich(logEvent, _messageTemplateProcessor); }
        catch (Exception ex) { SelfLog.WriteLine(...); }
        _sink.Emit(logEvent);
    }

    // Level check — called before ANY work (avoids allocations for disabled levels)
    public bool IsEnabled(LogEventLevel level)
    {
        if (level < _minimumLevel) return false;
        return _levelSwitch == null || level >= _levelSwitch.MinimumLevel;
    }
}
```

---

## Key Patterns Extracted

### Pattern 1: Single-Type Pipeline (LogEvent flows through all stages)

Every stage operates on the same type: `LogEvent`. Enrichers mutate it in-place; filters inspect it; sinks consume it. No intermediate DTOs between stages.

```
LogEvent → Enricher(mutate) → Filter(inspect) → Sink(consume) → Formatter(render)
```

**For Posit:** When decomposing a pipeline spec, identify a single data type that all components accept. Each component interface takes the same type and either mutates it (enricher), gates it (filter), or consumes it (sink).

### Pattern 2: Decorator-Based Context Attachment (ForContext)

`ILogger.ForContext(enricher)` returns a NEW `ILogger` that wraps the parent as a sink. This is a decorator pattern — the child logger enriches events then delegates to the parent's sink.

```csharp
// ForContext creates a child Logger where `this` (parent) is the sink
return new Logger(_messageTemplateProcessor, _minimumLevel, _levelSwitch,
    this,              // ← parent Logger as ILogEventSink
    enricher,          // ← the new contextual enricher
    null, _overrideMap);
```

**For Posit:** Context/scoping can be modeled as a decorator that wraps the downstream pipeline. The decorator implements the same interface as the component it wraps, so the caller doesn't know the difference.

### Pattern 3: Two-Tier Level Filtering (fast gate + custom filter)

Level filtering happens in TWO places with different semantics:
- **Tier 1 (Logger.IsEnabled):** Before LogEvent construction. O(1) enum comparison. Avoids all allocations for disabled levels. Checked on every Write call.
- **Tier 2 (ILogEventFilter):** After enrichment, before sink emission. Runs per-filter in a loop. Can inspect the fully-enriched LogEvent including all properties. Short-circuits on first `false`.

```
Write(level, ...) → IsEnabled(level)? [Tier 1: fast]
                     ↓ yes
                   construct LogEvent
                     ↓
                   enrich
                     ↓
                   FilteringSink: foreach filter.IsEnabled(logEvent)? [Tier 2: rich]
                     ↓ all pass
                   sink.Emit(logEvent)
```

**For Posit:** Separate "cheap gate" filters (enum/level comparison, no allocation) from "content-based" filters (inspect full data). Run cheap gates first, before any data transformation.

### Pattern 4: Safe Aggregate (fan-out with error isolation)

Both enrichers and sinks use a "Safe Aggregate" wrapper that iterates a list and try/catches each item individually. One failing enricher or sink does NOT break the others.

```csharp
// SafeAggregateEnricher — per-enricher try/catch
foreach (var enricher in _enrichers) {
    try { enricher.Enrich(logEvent, propertyFactory); }
    catch (Exception ex) { SelfLog.WriteLine(...); }  // log and continue
}

// SafeAggregateSink — per-sink try/catch
foreach (var sink in _sinks) {
    try { sink.Emit(logEvent); }
    catch (Exception ex) { SelfLog.WriteLine(...); }  // log and continue
}
```

**For Posit:** When a stage fans out to N components, wrap the fan-out in a safe-aggregate that isolates failures per item. The aggregate implements the same interface as its items (ILogEventEnricher / ILogEventSink), so it's transparent to the pipeline.

### Pattern 5: Factory-Property-Value Chain (structured data destructuring)

Structured data flows through a three-level factory chain:

```
ILogEventPropertyFactory.CreateProperty(name, value, destructureObjects)
    → ILogEventPropertyValueFactory.CreatePropertyValue(value, destructureObjects)
        → IDestructuringPolicy.TryDestructure(value, factory, out result)
            → LogEventPropertyValue (ScalarValue | SequenceValue | StructureValue | DictionaryValue)
```

The `MessageTemplateProcessor` class implements BOTH `ILogEventPropertyFactory` and `ILogEventPropertyValueFactory`, delegating to `PropertyValueConverter` internally. This is a single concrete class bridging two interfaces.

**For Posit:** When structured data needs policy-driven conversion, use a chain of factory interfaces where each level delegates to the next. A single class can implement multiple factory interfaces to avoid indirection.

### Pattern 6: Fluent Builder with Late Materialization (LoggerConfiguration)

`LoggerConfiguration` accumulates sinks, enrichers, filters, and level settings via fluent methods. The actual pipeline is only constructed when `CreateLogger()` is called — at that point the wiring is deterministic:

```
sinks → SafeAggregateSink
      → (if filters) FilteringSink(aggregate, filters)
enrichers → SafeAggregateEnricher (or EmptyEnricher)
converter → MessageTemplateProcessor
      → new Logger(processor, level, switch, sink, enricher, dispose, overrideMap)
```

**For Posit:** Configuration interfaces should be separate from runtime interfaces. A builder accumulates declarations; a materialization step wires them into a concrete pipeline. This lets the architect reason about the pipeline structure before code generation.

---

## Key Constraints

1. **LogEvent is the universal data type.** All pipeline stages accept LogEvent. Enrichers mutate it; filters inspect it; sinks consume it. Do NOT introduce intermediate types between stages.

2. **Level check before allocation.** `IsEnabled()` is called BEFORE the LogEvent is constructed. If the level is below minimum, no message template parsing, no property binding, no LogEvent allocation occurs. This is a deliberate performance optimization.

3. **Enrichment mutates in-place.** `ILogEventEnricher.Enrich(logEvent, propertyFactory)` adds/modifies properties directly on the LogEvent via `logEvent.AddOrUpdateProperty()`. The enricher does NOT return a new LogEvent.

4. **Filters short-circuit on first rejection.** `FilteringSink` iterates filters in order. The first `IsEnabled(logEvent) == false` drops the event immediately — remaining filters and the sink are not called.

5. **Safe aggregates isolate failures.** One failing enricher does not prevent other enrichers from running. One failing sink does not prevent other sinks from receiving the event. Exceptions are caught and logged to SelfLog.

6. **Logger is both ILogger and ILogEventSink.** The `Logger` class implements `ILogger` (public API) AND `ILogEventSink` (pipeline internal). This dual role enables the ForContext decorator pattern — a child Logger writes to its parent via the sink interface.

7. **ITextFormatter is used INSIDE sinks, not in the pipeline.** The pipeline itself does not format. Each sink decides how to format (some use ITextFormatter, others like JSON sinks use their own rendering). The formatter is a sink-internal detail.

8. **Two sink variants: synchronous (ILogEventSink) and batched async (IBatchedLogEventSink).** The batched variant accepts `IReadOnlyCollection<LogEvent>` and returns `Task`, enabling high-throughput network sinks.

---

## Applicability to Posit Code Generation

This pattern is directly applicable when a spec requires:
- A pipeline with ordered stages (enrich → filter → sink)
- Multiple components operating on the same data type
- A fast early-exit gate before expensive processing
- Fan-out to multiple destinations with error isolation
- Contextual decorators that attach metadata
- Policy-driven data conversion (destructuring)

**Interface decomposition template:**
- `IEntry` — public API, convenience methods, level/flag check (ILogger)
- `IEnricher` — mutate the data object in-place (ILogEventEnricher)
- `IFilter` — inspect the data object, return bool (ILogEventFilter)
- `ISink` — consume the data object (ILogEventSink)
- `IFormatter` — render the data object to output (ITextFormatter)
- `IFactory` — create typed values from raw input (ILogEventPropertyFactory)
- `IPolicy` — pluggable conversion strategy (IDestructuringPolicy)