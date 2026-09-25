# AutoHttpClient.Generator

[![NuGet](https://img.shields.io/nuget/v/AutoHttpClient.Generator.svg)](https://www.nuget.org/packages/AutoHttpClient.Generator)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AutoHttpClient.Generator.svg)](https://www.nuget.org/packages/AutoHttpClient.Generator)
[![CI](https://github.com/Swevo/AutoHttpClient.Generator/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoHttpClient.Generator/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10 Ready](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](#)

AutoHttpClient.Generator is an **AOT-safe, compile-time typed HTTP client** for .NET. Annotate an interface with `[HttpClient]`, decorate methods with `[Get]`, `[Post]`, `[Put]`, `[Delete]`, or `[Patch]`, and the generator emits a strongly-typed implementation plus DI registration at build time.

## Why AutoHttpClient.Generator?

- **Compile-time generated clients** — no dynamic proxy generation, no reflection-heavy dispatch layer
- **AOT-safe request dispatch** — generated C# calls `HttpClient` directly with no reflection-based proxy; see [`samples/AotBenchmark/BENCHMARK.md`](samples/AotBenchmark/BENCHMARK.md) for measured trim-warning results and known remaining gaps
- **Minimal ceremony** — plain interfaces plus attributes, no hand-written wrappers
- **DI-ready** — `AddAutoHttpClients()` registers every generated client for `IServiceCollection`
- **Strongly typed** — route values, query parameters, headers, and JSON bodies all come from your method signature

### Why not Refit or RestSharp?

- **Refit** (v16+) has also moved to a Roslyn source generator and ships an official Native AOT path (`RestService.ForGenerated<T>` + a `JsonSerializerContext`), so it's no longer purely runtime-proxy based. The real differences today are ergonomics: AutoHttpClient.Generator's generated client can be constructed directly (`new`) or resolved from DI with zero extra AOT-specific API surface, has build-time diagnostics for common mistakes (see [Diagnostics](#diagnostics)), and ships an OpenAPI scaffolding tool. See [`samples/AotBenchmark/BENCHMARK.md`](samples/AotBenchmark/BENCHMARK.md) for a measured, head-to-head trim/AOT comparison — including places where AutoHttpClient.Generator still has warnings Refit doesn't.
- **RestSharp** is a runtime HTTP abstraction with reflection-oriented configuration rather than compile-time emitted clients
- **AutoHttpClient.Generator** keeps everything as generated source in your build output: explicit, trim-friendly, and (for the request-building path) zero-reflection

## Installation

```bash
dotnet add package AutoHttpClient.Generator
```

Then register the generated clients:

```csharp
builder.Services.AddAutoHttpClients();
```

## Quick start

```csharp
using AutoHttpClient;

[HttpClient]
public interface IOrdersApi
{
    [Get("/api/orders/{id}")]
    Task<Order?> GetOrderAsync(int id, CancellationToken ct = default);

    [Get("/api/orders")]
    Task<List<Order>> GetOrdersAsync([Query("status")] string? status = null, CancellationToken ct = default);

    [Post("/api/orders")]
    Task<Order> CreateOrderAsync([Body] CreateOrderRequest request, CancellationToken ct = default);

    [Put("/api/orders/{id}")]
    Task<Order> UpdateOrderAsync(int id, [Body] UpdateOrderRequest request, CancellationToken ct = default);

    [Delete("/api/orders/{id}")]
    Task DeleteOrderAsync(int id, CancellationToken ct = default);

    [Get("/api/orders/{id}/status")]
    Task<HttpResponseMessage> GetOrderStatusRawAsync(int id, CancellationToken ct = default);
}
```

Register the generated implementation:

```csharp
builder.Services.AddAutoHttpClients();
```

This emits an internal sealed client implementation and a DI registration similar to:

```csharp
services.AddHttpClient<IOrdersApi, OrdersApiClient>();
```

## Parameter attributes

AutoHttpClient.Generator classifies parameters using these rules:

| Parameter style | Behavior |
|---|---|
| `[Body]` | Serialized as JSON request content |
| `[Query("name")]` | Added to the query string using the provided name |
| `[Query]` or unattributed non-route parameter | Added to the query string using the parameter name |
| Array/`IEnumerable<T>` query parameter (not `string`) | Expanded into one repeated `name=value` entry per element |
| `[Header("X-Name")]` | Added as an HTTP header |
| `[HeaderCollection]` | Expands an `IDictionary<string, string?>` (or any `IEnumerable<KeyValuePair<string, string?>>`) parameter into one header per entry |
| `[QueryMap]` | Expands an `IDictionary<string, string?>` (or any `IEnumerable<KeyValuePair<string, string?>>`) parameter into one query string entry per pair |
| Route parameter | Any parameter whose name appears in the route template, e.g. `{id}` |
| `CancellationToken` | Passed through to `HttpClient` and JSON helpers |

### Examples

```csharp
[Get("/api/orders")]
Task<List<Order>> GetOrdersAsync([Query("status")] string? status = null, int page = 1, CancellationToken ct = default);

[Post("/api/orders")]
Task<Order> CreateOrderAsync([Body] CreateOrderRequest request, [Header("X-Tenant")] string tenant, CancellationToken ct = default);

[Get("/api/orders/{id}")]
Task<Order?> GetOrderAsync(int id, CancellationToken ct = default);

[Get("/api/orders")]
Task<List<Order>> SearchOrdersAsync([QueryMap] IDictionary<string, string?> filters, CancellationToken ct = default);

[Get("/api/orders")]
Task<List<Order>> GetOrdersForTenantAsync([HeaderCollection] IDictionary<string, string?> headers, CancellationToken ct = default);
```

## Static headers

Apply constant headers to every request generated for an interface or a specific method with `[Headers("Name: Value")]`, similar to Refit:

```csharp
using AutoHttpClient;

[HttpClient(BaseAddress = "https://api.example.com")]
[Headers("X-Api-Version: 1.0")]
public interface IOrdersApi
{
    [Get("/api/orders")]
    [Headers("Accept: application/json")]
    Task<List<Order>> GetOrdersAsync(CancellationToken ct = default);
}
```

Method-level `[Headers]` take priority over interface-level ones when the same header name is declared in both places. `[Headers]` can be applied multiple times on the same target.

## Multipart form uploads

Mark a method `[Multipart]` and decorate its parameters with `[Part]` to send a `multipart/form-data` request — useful for file uploads:

```csharp
using AutoHttpClient;

[HttpClient(BaseAddress = "https://api.example.com")]
public interface IUploadsApi
{
    [Post("/api/uploads")]
    [Multipart]
    Task<UploadResult> UploadAsync(
        [Part("file", "photo.png")] Stream file,
        [Part("description")] string description,
        [Part] UploadMetadata metadata,
        CancellationToken ct = default);
}
```

Part parameter types are handled automatically:

| Parameter type | Generated content |
|---|---|
| `string` | `StringContent` |
| `byte[]` | `ByteArrayContent` |
| `Stream` (or subclass) | `StreamContent` |
| Anything else | JSON-serialized via `JsonContent.Create` |

`[Part(name, fileName)]` controls the form field name and, optionally, the file name sent to the server; both default to the parameter name / no file name. A `[Multipart]` method cannot also declare a `[Body]` parameter (`AH004`).

## Return types

| Return type | Generated behavior |
|---|---|
| `Task` | Sends the request and throws `ApiException` on a non-success status code |
| `Task<T>` | Sends the request, checks for success, and deserializes JSON with `ReadFromJsonAsync<T>()` |
| `Task<T?>` | Same as `Task<T>` but preserves nullable result types |
| `Task<HttpResponseMessage>` | Returns the raw response without any success check |

## Error handling

Non-success responses throw `AutoHttpClient.ApiException` (instead of a bare `EnsureSuccessStatusCode()` call) so you don't lose the response body:

```csharp
try
{
    var order = await ordersApi.GetOrderAsync(404, ct);
}
catch (AutoHttpClient.ApiException ex)
{
    // ex.StatusCode, ex.ReasonPhrase, ex.Content (raw response body, best-effort)
}
```

If you need the raw `HttpResponseMessage` instead (no exception thrown), use a `Task<HttpResponseMessage>` return type.

## BaseAddress configuration

You can configure a base address directly on the interface attribute:

```csharp
using AutoHttpClient;

[HttpClient(BaseAddress = "https://api.example.com")]
public interface IOrdersApi
{
    [Get("/api/orders")]
    Task<List<Order>> GetOrdersAsync(CancellationToken ct = default);
}
```

The generated DI registration configures the typed client:

```csharp
services.AddHttpClient<IOrdersApi, OrdersApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.example.com");
});
```

## Scaffolding from OpenAPI/Swagger

AutoHttpClient.Generator now includes a small repo-side scaffolding tool for converting an OpenAPI/Swagger JSON document into a partial interface decorated with AutoHttpClient attributes.

Run it with:

```bash
dotnet run --project tools/AutoHttpClient.OpenApiScaffold -- --input swagger.json --output IMyApiClient.g.cs --namespace MyApp.Clients --interface-name IMyApiClient
```

The generated file is a one-time scaffold that you add to your project, then the existing `AutoHttpClient.Generator` source generator consumes it normally.

### What it generates

- `[HttpClient]` or `[HttpClient(BaseAddress = "...")]` when the spec declares a simple server URL
- `[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Patch]` based on each OpenAPI operation
- Route parameters as normal method parameters
- Query parameters as `[Query("name")]`
- Request bodies as `[Body]`
- `Task<T>` return types using referenced schema names where possible

### Current scope / limitations

- Optimized for common OpenAPI 3 JSON documents
- Swagger/OpenAPI 2 documents may work for basic paths/operations, but v3 is the primary target
- Best support is for JSON request/response bodies with named schemas, simple path/query/header parameters, and standard HTTP verbs
- Inline/anonymous schemas fall back to `JsonElement` (or collections/dictionaries of known types where possible)
- Advanced OpenAPI features such as `oneOf`, `anyOf`, callbacks, multipart form uploads, and full DTO generation are not scaffolded yet
- Named schemas are used as C# type names in the generated interface; you still need matching DTO types in your project

## Comparison

| Feature | AutoHttpClient.Generator | Refit (v16+) | RestSharp |
|---|---|---|---|
| Compile-time generated client | ✅ | ✅ (also generator-based) | ❌ |
| AOT-safe out of the box (no reflection-based JSON) | ⚠️ 2 known trim warnings remain — see [BENCHMARK.md](samples/AotBenchmark/BENCHMARK.md) | ✅ with `JsonSerializerContext` | ❌ |
| Zero reflection dispatch | ✅ | ✅ | ❌ |
| Native `HttpClient` typed client DI | ✅ | ✅ | ⚠️ manual |
| Interface-first API | ✅ | ✅ | ❌ |
| OpenAPI/Swagger scaffolding tool | ✅ (repo tool) | ✅ | ⚠️ varies |
| Build-time diagnostics | ✅ | Limited | ❌ |
| Typed exception with response body on failure | ✅ (`ApiException`) | ✅ (`ApiException`) | ⚠️ manual |
| Collection query parameter expansion | ✅ | ✅ | ⚠️ manual |
| Multipart/form-data uploads | ✅ | ✅ | ⚠️ manual |

## Diagnostics

| Code | Severity | Message |
|---|---|---|
| `AH001` | Warning | Method on a `[HttpClient]` interface has no HTTP method attribute and will not be generated. |
| `AH002` | Warning | Route template parameter has no matching method parameter. |
| `AH003` | Error | Method has multiple `[Body]` parameters; only one is allowed. |
| `AH004` | Error | Method is marked `[Multipart]` but also has a `[Body]` parameter. |
| `AH005` | Warning | Parameter is marked `[Part]` but its method is not marked `[Multipart]`. |

## Generated attributes

The package emits these attributes at post-initialization time:

- `HttpClientAttribute`
- `GetAttribute`
- `PostAttribute`
- `PutAttribute`
- `DeleteAttribute`
- `PatchAttribute`
- `BodyAttribute`
- `QueryAttribute`
- `HeaderAttribute`
- `HeadersAttribute`
- `HeaderCollectionAttribute`
- `QueryMapAttribute`
- `MultipartAttribute`
- `PartAttribute`
- `ApiException`

## Migrating from Refit

AutoHttpClient.Generator uses the same interface-first approach as Refit. Migration is mostly a find-and-replace of attributes.

### 1. Install and remove Refit

```bash
dotnet add package AutoHttpClient.Generator
dotnet remove package Refit
dotnet remove package Refit.HttpClientFactory
```

### 2. Replace Refit attributes with AutoHttpClient attributes

```csharp
// Before (Refit)
using Refit;

public interface IOrdersApi
{
    [Get("/api/orders/{id}")]
    Task<Order?> GetOrderAsync(int id, CancellationToken ct = default);

    [Post("/api/orders")]
    Task<Order> CreateOrderAsync([Body] CreateOrderRequest request, CancellationToken ct = default);

    [Get("/api/orders")]
    Task<List<Order>> GetOrdersAsync([AliasAs("status")] string? status = null);
}

// After (AutoHttpClient.Generator)
using AutoHttpClient;

[HttpClient]
public interface IOrdersApi
{
    [Get("/api/orders/{id}")]
    Task<Order?> GetOrderAsync(int id, CancellationToken ct = default);

    [Post("/api/orders")]
    Task<Order> CreateOrderAsync([Body] CreateOrderRequest request, CancellationToken ct = default);

    [Get("/api/orders")]
    Task<List<Order>> GetOrdersAsync([Query("status")] string? status = null);
}
```

### 3. Update DI registration

```csharp
// Before (Refit)
builder.Services.AddRefitClient<IOrdersApi>()
    .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://api.example.com"));

// After (AutoHttpClient.Generator)
[HttpClient(BaseAddress = "https://api.example.com")]
public interface IOrdersApi { ... }

builder.Services.AddAutoHttpClients();
```

### Attribute mapping

| Refit | AutoHttpClient.Generator |
|---|---|
| `[Get("/path")]` | `[Get("/path")]` |
| `[Post("/path")]` | `[Post("/path")]` |
| `[Put("/path")]` | `[Put("/path")]` |
| `[Delete("/path")]` | `[Delete("/path")]` |
| `[Patch("/path")]` | `[Patch("/path")]` |
| `[Body]` | `[Body]` |
| `[AliasAs("name")]` | `[Query("name")]` |
| `[Header("X-Name")]` | `[Header("X-Name")]` |
| `[Headers("X: Y")]` | `[Headers("X: Y")]` |
| `[HeaderCollection]` | `[HeaderCollection]` |
| `[Multipart]` / `[AttachmentName]` | `[Multipart]` / `[Part(name, fileName)]` |
| `[Authorize]` | Use `[Header("Authorization")]` |

### What Refit supports that AutoHttpClient.Generator doesn't (yet)

- `IObservable<T>` return types
- Custom `JsonSerializerSettings` per method
- Fully warning-free Native AOT/trim publishing when using a `JsonSerializerContext` — AutoHttpClient.Generator still emits 2 trim warnings from the generic `ReadFromJsonAsync<T>(..., JsonSerializerOptions, ...)` deserialization call and the DI typed-client registration path (measured in [`samples/AotBenchmark/BENCHMARK.md`](samples/AotBenchmark/BENCHMARK.md)); closing this gap by generating `JsonTypeInfo<T>`-based calls is tracked as follow-up work

For projects using any of these heavily, hold off on migrating until support lands.

## Also by the same author

> 🌐 Full suite overview: **[swevo.github.io](https://swevo.github.io/)**

| Package | Description |
|---|---|
| [**AutoLog.Generator**](https://github.com/Swevo/AutoLog.Generator) | Compile-time high-performance logging — `[Log(Level, Message)]` on a partial method generates `LoggerMessage.Define`. AOT-safe. |
| [**AutoDispatch.Generator**](https://github.com/Swevo/AutoDispatch.Generator) | Compile-time CQRS dispatcher — `[Handler]` generates a strongly-typed `IDispatcher`. No MediatR, no reflection. |
| [**AutoWire**](https://github.com/Swevo/AutoWire) | Compile-time DI auto-registration — `[Scoped]`/`[Singleton]`/`[Transient]` generates `IServiceCollection` registration code. |
| [**AutoMap.Generator**](https://github.com/Swevo/AutoMap.Generator) | Compile-time object mapping with generated extension methods. AOT-safe AutoMapper alternative. |
| [**AutoValidate.Generator**](https://github.com/Swevo/AutoValidate.Generator) | Compile-time FluentValidation wiring — discovers validators and generates `AddValidators()`. |
| [**AutoResult.Generator**](https://github.com/Swevo/AutoResult.Generator) | Compile-time `Result<T>` — `[TryWrap]` generates `Try*()` wrappers for every public method. |
| [**AutoQuery.Generator**](https://github.com/Swevo/AutoQuery.Generator) | Compile-time LINQ query specs — `[QuerySpec]` generates a strongly-typed `Apply(IQueryable<T>)`. |

## Related Packages

| Package | Downloads | Description |
|---|---|---|
| [AutoWire](https://www.nuget.org/packages/AutoWire) | [![Downloads](https://img.shields.io/nuget/dt/AutoWire.svg)](https://www.nuget.org/packages/AutoWire) | Compile-time dependency injection auto-registration for  |
| [AutoMap.Generator](https://www.nuget.org/packages/AutoMap.Generator) | [![Downloads](https://img.shields.io/nuget/dt/AutoMap.Generator.svg)](https://www.nuget.org/packages/AutoMap.Generator) | Compile-time object mapping for  |
| [AutoQuery.Generator](https://www.nuget.org/packages/AutoQuery.Generator) | [![Downloads](https://img.shields.io/nuget/dt/AutoQuery.Generator.svg)](https://www.nuget.org/packages/AutoQuery.Generator) | Compile-time query composition for IQueryable using Roslyn incremental source generators |
| [AutoArchitecture](https://www.nuget.org/packages/AutoArchitecture) | [![Downloads](https://img.shields.io/nuget/dt/AutoArchitecture.svg)](https://www.nuget.org/packages/AutoArchitecture) | Compile-time architecture/dependency-rule enforcement for  |
| [AutoDispatch.Generator](https://www.nuget.org/packages/AutoDispatch.Generator) | [![Downloads](https://img.shields.io/nuget/dt/AutoDispatch.Generator.svg)](https://www.nuget.org/packages/AutoDispatch.Generator) | Compile-time CQRS dispatcher for  |
| [AutoLog.Generator](https://www.nuget.org/packages/AutoLog.Generator) | [![Downloads](https://img.shields.io/nuget/dt/AutoLog.Generator.svg)](https://www.nuget.org/packages/AutoLog.Generator) | Compile-time high-performance logging for  |
| [AutoValidate.Generator](https://www.nuget.org/packages/AutoValidate.Generator) | [![Downloads](https://img.shields.io/nuget/dt/AutoValidate.Generator.svg)](https://www.nuget.org/packages/AutoValidate.Generator) | Compile-time FluentValidation wiring for  |

---

## License

MIT
