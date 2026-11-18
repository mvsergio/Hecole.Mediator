# Benchmarks

Cost of `Send` and `Publish` in Hecole.Mediator 2.0, measured against 1.3.0 in the same session on the same machine.
Read the [limits](#limits) before using these numbers.

## What was measured

Handlers and behaviors do no work: the request handler returns a completed `Task<string>`, notification handlers return
`Task.CompletedTask`, and test behaviors only call `next()`. The numbers are therefore the cost of the mediator plus the
cost of `Microsoft.Extensions.DependencyInjection` resolving handlers and behaviors, not the cost of an application.

| Scenario | Setup |
|---|---|
| Handler called directly | `handler.Handle(request, default)` with no mediator; floor of the measurement |
| `Send`, 0 / 1 / 4 behaviors | Handler registered with `AddRequestHandler` (1.3.0: added to the container); 1 or 4 distinct pass-through open behaviors |
| `Send(object)` | Same request passed as `object` |
| `Send`, request without response | `IRequest` and `IRequestHandler<TRequest>` |
| `Send`, handler from scan | `cfg.RegisterServicesFromAssemblyContaining<T>()` |
| `Send`, handler only in the container | `services.AddTransient<IRequestHandler<A, B>, H>()`, no scan, no `AddRequestHandler` |
| `Publish`, 1 / 3 handlers | Three different handler classes; once with `ForeachAwaitPublisher` (2.0 default) and once with `TaskWhenAllPublisher` |
| 4 built-in behaviors | `UnhandledExceptionBehavior`, `LoggingBehavior`, `PerformanceBehavior`, `ValidationBehavior`; every `ILogger<T>` is `NullLogger<T>` (logging off); the request has no validation rules |

In every scenario the container is built once, the mediator is resolved once from a scope and reused for every call.

## Method

| Item | Value |
|---|---|
| Harness | [`benchmarks/Hecole.Mediator.Benchmarks`](../benchmarks/Hecole.Mediator.Benchmarks) (2.0); a copy with the same `Harness.cs` and the same scenarios compiled against 1.3.0 |
| Timing | `Stopwatch` around 300,000 sequential awaited calls; 30,000 warm-up calls first |
| Allocation | `GC.GetAllocatedBytesForCurrentThread()` before and after each round, divided by the number of calls |
| Rounds | 7 per scenario, full GC before each; the median round is kept |
| Runs | 5 runs per version, interleaved (1.3.0, 2.0, 1.3.0, 2.0, ...) so that machine noise affects both versions alike. The tables show the lowest and highest of the 5 medians, and the median of the 5 |
| Process | Release build, process priority High, workstation GC, tiered PGO on (default) |
| 1.3.0 build | Source of tag `v1.3.0` (`git archive v1.3.0`), built in Release for `net10.0` |

To run the 2.0 harness:

```bash
dotnet run -c Release --project benchmarks/Hecole.Mediator.Benchmarks
```

Arguments filter the scenarios by key, for example `send-0 publish-1-foreach`.

## Environment

| Item | Value |
|---|---|
| Date | 2026-11-17 |
| CPU | Intel Core i7-1165G7 (4 cores, 8 logical processors), laptop |
| Memory | 32 GB |
| OS | Windows 11, 10.0.26300, x64 |
| .NET | SDK 10.0.401, runtime 10.0.15 |
| Container | `Microsoft.Extensions.DependencyInjection` 10.0.0 for both versions |
| Load | Other applications were running; no attempt was made to isolate the machine |

## Results

Time in nanoseconds per call: lowest–highest of 5 run medians (median of the 5). Allocation in bytes per call was the same
in every run of a given scenario.

| Scenario | 1.3.0 ns/op | 2.0 ns/op | 1.3.0 B/op | 2.0 B/op |
|---|---|---|---|---|
| Handler called directly | 9–12 (11) | 7–13 (12) | 0 | 0 |
| `Send`, 0 behaviors | 1,477–1,967 (1,852) | 79–101 (92) | 768 | 24 |
| `Send`, 1 behavior | 2,549–4,178 (2,736) | 182–216 (188) | 1,376 | 184 |
| `Send`, 4 behaviors | 2,949–4,524 (3,347) | 373–418 (385) | 1,856 | 592 |
| `Send(object)` | not available | 125–233 (134) | — | 96 |
| `Send`, request without response | not available | 168–239 (183) | — | 24 |
| `Send`, handler from scan | see note 1 | 90–189 (97) | — | 24 |
| `Send`, handler only in the container | same as `Send`, 0 behaviors | 117–199 (118) | — | 24 |
| `Publish`, 1 handler, `ForeachAwaitPublisher` | not available | 461–731 (482) | — | 184 |
| `Publish`, 3 handlers, `ForeachAwaitPublisher` | not available | 1,149–1,935 (1,171) | — | 456 |
| `Publish`, 1 handler, `Task.WhenAll` | 1,161–1,987 (1,344) | 508–797 (613) | 376 | 248 |
| `Publish`, 3 handlers, `Task.WhenAll` | 1,635–2,378 (1,817) | 1,330–2,142 (1,372) | 672 | 608 |
| 4 built-in behaviors, logging off | 3,759–5,765 (4,399) | 1,254–1,972 (1,356) | 2,352 | 1,336 |

Notes:

1. 1.3.0 always resolves the handler from the container, however it was registered, so "handler only in the container"
   is its `Send, 0 behaviors` row. Its assembly scan also registers every pipeline behavior of the scanned assembly, so a
   scan of the benchmark assembly is not comparable and was not measured.
2. "`Task.WhenAll`" is `TaskWhenAllPublisher` in 2.0 and the only notification strategy of 1.3.0. 2.0 runs notification
   handlers one at a time by default (`ForeachAwaitPublisher`); 1.3.0 has no such mode.
3. The 4 built-in behaviors are not the same code in both versions. In 1.3.0, validation runs a third-party package with no
   validator registered (the behavior only checks that the list is empty); in 2.0 it is a DataAnnotations
   `Validator.TryValidateObject` call on a request without attributes. 1.3.0 logging calls `LogInformation` with a
   `NullLogger`; 2.0 uses `LoggerMessage`-generated methods that check `IsEnabled` first.
4. The 1.3.0 numbers with 1 and 4 behaviors are higher than in the earlier 1.3.0 baseline (1,869–1,986 ns), which
   registered the same behavior type four times; here four distinct types were used in both versions, and the machine
   load was different. Compare rows within this table, not with other measurements.
5. Run 3 of 2.0 was slower in most scenarios (it accounts for most of the upper ends of the ranges); it was kept.

## Reading the results

What the table supports, for this machine and these scenarios:

| Statement | Basis |
|---|---|
| `Send` with 0, 1 and 4 behaviors is faster in 2.0 and allocates less | The 2.0 ranges are below the 1.3.0 ranges, with no overlap, in all three rows; allocation 24 / 184 / 592 B against 768 / 1,376 / 1,856 B |
| A handler found by scan costs the same per call as one registered with `AddRequestHandler` | Medians 97 and 92 ns, overlapping ranges, same allocation |
| A handler registered only in the container costs about 25 ns more per call | Median 118 against 92 ns; same allocation. The executor is created on first use and looked up in a second dictionary |
| A request without response is slower than a request with response | Median 183 against 92 ns, ranges do not overlap; same allocation |
| `Publish` with 1 handler is faster in 2.0 and allocates less | `Task.WhenAll` rows: ranges 508–797 against 1,161–1,987 ns, no overlap; 248 against 376 B |
| `Publish` with 3 handlers allocates less in 2.0 | 608 against 672 B. Time: the ranges overlap (1,330–2,142 against 1,635–2,378 ns), so no time difference is claimed |
| The 4 built-in behaviors cost less in 2.0 with logging off | Ranges do not overlap; 1,336 against 2,352 B. Note 3 applies: the code differs |

## Limits

- **This is not a statistical benchmark harness.** There is no process isolation per benchmark, no outlier analysis, no confidence
  interval and no hardware counters. The ranges come from 5 interleaved runs on a laptop that was also running other
  applications. Differences smaller than the ranges are noise.
- One machine, one operating system, one runtime version, one container (`Microsoft.Extensions.DependencyInjection`).
  Other containers can change the cost of resolving handlers and behaviors, which is most of the cost measured here.
- Calls are sequential on one thread and every handler completes synchronously. Contention, real I/O, asynchronous
  completion and cold start (first call per request type, container build, scan) were not measured.
- Allocation is the per-call average of the measuring thread; allocation on other threads is not counted.
- The handlers do no work. In an application, the handler's own work is added to every number above.
