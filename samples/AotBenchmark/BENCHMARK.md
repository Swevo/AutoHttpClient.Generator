# AutoHttpClient.Generator vs Refit — AOT/trim benchmark

This is a small, honest, reproducible comparison between AutoHttpClient.Generator and
Refit 16.1.0, both targeting `net10.0`, both using a `System.Text.Json`
`JsonSerializerContext` (no reflection-based JSON), and both publishing self-contained +
fully trimmed (`PublishTrimmed=true`, `TrimMode=full`).

**Note on scope:** this machine does not have the full "Desktop development with C++"
Visual Studio workload installed (only the MSVC compiler, not the AOT linker
prerequisites), so a true Native AOT (`PublishAot=true`) link could not be completed here.
The results below use `PublishTrimmed` instead, which still exercises the same
trim-analyzer warnings that Native AOT relies on and produces a genuinely
self-contained, IL-trimmed binary — just without the final native code-gen step. If you
have the full AOT prerequisites installed, both sample apps already have
`PublishAot=true` set in their `.csproj` and can be published as-is.

Sample apps live in `samples/AotBenchmark/AutoHttpClientApp` and
`samples/AotBenchmark/RefitApp`. Each declares one `[Get("/ping")]` method returning a
`PingResult`, backed by a source-generated `JsonSerializerContext`.

## Results (win-x64, self-contained, trimmed, Release)

| | AutoHttpClient.Generator | Refit 16.1.0 |
|---|---|---|
| Build/publish warnings | 3 (see below) | 0 |
| Published file count | 40 | 50 |
| Published size | 19.62 MB | 21.14 MB |
| Cold client construction | ~95 ms | ~98 ms |

Construction timing is dominated by .NET self-contained startup/JIT warm-up on this
machine and is within noise of each other — **do not read a meaningful performance
difference into that number**. Size and warning count are the more meaningful data
points here.

## The 3 remaining AutoHttpClient warnings

1. `IL2026` on the generated typed-client DI registration
   (`AddHttpClient<TClient, TImplementation>`), because the generated client still
   exposes a convenience constructor, `Ctor(HttpClient)`, that falls back to
   `JsonSerializerOptions.Web` and is explicitly annotated
   `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`. `AddHttpClient<TClient,
   TImplementation>` requires all public constructors of `TImplementation` to be
   trim-safe as a matter of its own generic constraints, so this warning surfaces
   at the registration call site even though the DI-friendly 2-argument constructor
   (`Ctor(HttpClient, JsonSerializerOptions)`) is fully AOT-safe and is what gets
   selected automatically once a `JsonSerializerOptions` is registered in the
   container. This is an intentional trade-off: dropping the convenience constructor
   entirely would make `AddAutoHttpClients()` no longer "just work" out of the box.
2. and 3. `IL2026`/`IL3050` from the generated response-deserialization call,
   `HttpContent.ReadFromJsonAsync<T>(HttpContent, JsonSerializerOptions, ...)`. This is
   the generic (reflection-capable) overload; the fully AOT-safe overload takes a
   `JsonTypeInfo<T>` instead of a `JsonSerializerOptions`. AutoHttpClient does not yet
   generate per-method `JsonTypeInfo<T>` resolution the way Refit's generator does when
   given a `JsonSerializerContext` — **this is a known, tracked gap**, not something this
   benchmark papers over.

Refit's generated code resolves `JsonTypeInfo<T>` directly from the supplied
`JsonSerializerContext` at each call site (via `RestService.ForGenerated<T>(client,
SampleJsonContext.Default)`), which is why its trimmed publish has zero warnings.
Closing warnings 2–3 above is the next concrete step for full AOT parity and is tracked
as follow-up work — see the main `README.md` comparison table.

## Reproducing

```powershell
cd samples/AotBenchmark/AutoHttpClientApp
dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:TrimMode=full
# or, with full Native AOT prerequisites installed:
dotnet publish -c Release -r win-x64 -p:PublishAot=true --self-contained

cd ../RefitApp
dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:TrimMode=full
```
