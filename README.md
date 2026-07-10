# AutoHttpClient.Generator

[![NuGet](https://img.shields.io/nuget/v/AutoHttpClient.Generator.svg)](https://www.nuget.org/packages/AutoHttpClient.Generator)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AutoHttpClient.Generator.svg)](https://www.nuget.org/packages/AutoHttpClient.Generator)
[![CI](https://github.com/Swevo/AutoHttpClient.Generator/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoHttpClient.Generator/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

AutoHttpClient.Generator is an **AOT-safe, compile-time typed HTTP client** for .NET. Annotate an interface with `[HttpClient]`, decorate methods with `[Get]`, `[Post]`, `[Put]`, `[Delete]`, or `[Patch]`, and the generator emits a strongly-typed implementation plus DI registration at build time.

## Why AutoHttpClient.Generator?

- **Compile-time generated clients** — no dynamic proxy generation, no reflection-heavy dispatch layer
- **AOT-safe** — generated C# calls `HttpClient` directly, so it works cleanly with native AOT scenarios
- **Minimal ceremony** — plain interfaces plus attributes, no hand-written wrappers
- **DI-ready** — `AddAutoHttpClients()` registers every generated client for `IServiceCollection`
- **Strongly typed** — route values, query parameters, headers, and JSON bodies all come from your method signature

### Why not Refit or RestSharp?

- **Refit** is ergonomic, but relies on runtime plumbing and generated proxy behavior that is less predictable in AOT-focused deployments
- **RestSharp** is a runtime HTTP abstraction with reflection-oriented configuration rather than compile-time emitted clients
- **AutoHttpClient.Generator** keeps everything as generated source in your build output: explicit, trim-friendly, and zero-reflection

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
| `[Header("X-Name")]` | Added as an HTTP header |
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
```

## Return types

| Return type | Generated behavior |
|---|---|
| `Task` | Sends the request and calls `EnsureSuccessStatusCode()` |
| `Task<T>` | Sends the request, ensures success, and deserializes JSON with `ReadFromJsonAsync<T>()` |
| `Task<T?>` | Same as `Task<T>` but preserves nullable result types |
| `Task<HttpResponseMessage>` | Returns the raw response without `EnsureSuccessStatusCode()` |

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

| Feature | AutoHttpClient.Generator | Refit | RestSharp |
|---|---|---|---|
| Compile-time generated client | ✅ | Partial/runtime proxy behavior | ❌ |
| AOT-safe | ✅ | ⚠️ depends on runtime behavior | ❌ |
| Zero reflection dispatch | ✅ | ⚠️ | ❌ |
| Native `HttpClient` typed client DI | ✅ | ✅ | ⚠️ manual |
| Interface-first API | ✅ | ✅ | ❌ |
| OpenAPI/Swagger scaffolding tool | ✅ (repo tool) | ✅ | ⚠️ varies |
| Build-time diagnostics | ✅ | Limited | ❌ |

## Diagnostics

| Code | Severity | Message |
|---|---|---|
| `AH001` | Warning | Method on a `[HttpClient]` interface has no HTTP method attribute and will not be generated. |
| `AH002` | Warning | Route template parameter has no matching method parameter. |
| `AH003` | Error | Method has multiple `[Body]` parameters; only one is allowed. |

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
| `[HeaderCollection]` | Not supported |
| `[Authorize]` | Use `[Header("Authorization")]` |

### What Refit supports that AutoHttpClient.Generator doesn't (yet)

- `[HeaderCollection]` dictionary headers
- `[Multipart]` / `[AttachmentName]` for multipart form uploads
- `IObservable<T>` return types
- Custom `JsonSerializerSettings` per method

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

## License

MIT
