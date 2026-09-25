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
`samples/AotBenchmark/RefitApp`. Each declares a `[Get("/ping")]` method and a
`[Post("/ping")]` method with a JSON `[Body]`, both returning `PingResult`, backed by a
source-generated `JsonSerializerContext`. `AutoHttpClientApp` exercises both direct
`new PingApiClient(...)` construction and the `AddAutoHttpClients(jsonOptions)` DI
registration path, to prove both are warning-free.

## Results (win-x64, self-contained, trimmed, Release)

| | AutoHttpClient.Generator | Refit 16.1.0 |
|---|---|---|
| Build/publish warnings | **0** | 0 |
| Published file count | 51 | 50 |
| Published size | 19.96 MB | 21.14 MB |
| Cold client construction | ~178 ms | ~98 ms |

Construction timing is dominated by .NET self-contained startup/JIT warm-up on this
machine and varies run to run (AutoHttpClient's run also resolves a second client via a
full DI container, which Refit's sample doesn't do) — **do not read a meaningful
performance difference into that number**. Warning count and size are the more
meaningful, apples-to-apples data points here.

## How the 0-warning result was achieved

An earlier pass of this benchmark found 3 residual trim/AOT warnings on the
AutoHttpClient side. All three are now fixed by generating code that always resolves a
`JsonTypeInfo<T>` from the supplied `JsonSerializerOptions` (via
`options.GetTypeInfo(typeof(T))`, which is the documented trim-safe pattern) and calling
the `JsonTypeInfo<T>`-based overloads instead of the generic
`JsonSerializerOptions`-based ones:

1. **Response deserialization** now calls
   `HttpContentJsonExtensions.ReadFromJsonAsync(content, jsonTypeInfo, ct)` instead of
   the generic `ReadFromJsonAsync<T>(content, jsonSerializerOptions, ct)` overload.
2. **Request bodies** (`[Body]`/`[Part]`) now call
   `JsonContent.Create(value, jsonTypeInfo)` instead of
   `JsonContent.Create(value, options: jsonSerializerOptions)`.
3. **POST/PUT/PATCH convenience calls** now use the `JsonTypeInfo<T>` overloads of
   `PostAsJsonAsync`/`PutAsJsonAsync`/`PatchAsJsonAsync`.
4. **DI registration** (`AddAutoHttpClients(jsonOptions)`) no longer goes through
   `AddHttpClient<TClient, TImplementation>`, whose generic constraints require *every*
   public constructor of `TImplementation` to be trim-safe (including the
   reflection-based convenience constructor). It now uses
   `AddHttpClient(name).AddTypedClient<TClient>(httpClient => new TImplementation(httpClient, jsonOptions))`,
   an explicit factory delegate with no such constraint. The parameterless
   `AddAutoHttpClients()` convenience overload still exists for callers who don't have a
   `JsonSerializerOptions` handy, and is explicitly annotated
   `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` so *that* opt-in path — and only
   that path — surfaces a warning.

Refit's generated code resolves `JsonTypeInfo<T>` directly from the supplied
`JsonSerializerContext` at each call site (via `RestService.ForGenerated<T>(client,
SampleJsonContext.Default)`), which is the same underlying trim-safe pattern.

## Reproducing

```powershell
cd samples/AotBenchmark/AutoHttpClientApp
dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:TrimMode=full
# or, with full Native AOT prerequisites installed:
dotnet publish -c Release -r win-x64 -p:PublishAot=true --self-contained

cd ../RefitApp
dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:TrimMode=full
```
