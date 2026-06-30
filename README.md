# AutoHttpClient.Generator

[![NuGet](https://img.shields.io/nuget/v/AutoHttpClient.Generator.svg)](https://www.nuget.org/packages/AutoHttpClient.Generator)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AutoHttpClient.Generator.svg)](https://www.nuget.org/packages/AutoHttpClient.Generator)
[![CI](https://github.com/Swevo/AutoHttpClient.Generator/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoHttpClient.Generator/actions/workflows/build.yml)

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

## Comparison

| Feature | AutoHttpClient.Generator | Refit | RestSharp |
|---|---|---|---|
| Compile-time generated client | ✅ | Partial/runtime proxy behavior | ❌ |
| AOT-safe | ✅ | ⚠️ depends on runtime behavior | ❌ |
| Zero reflection dispatch | ✅ | ⚠️ | ❌ |
| Native `HttpClient` typed client DI | ✅ | ✅ | ⚠️ manual |
| Interface-first API | ✅ | ✅ | ❌ |
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

## Also by the same author

> 🌐 Full suite overview: **[swevo.github.io](https://swevo.github.io/)**

| Package | Description |
|---|---|
| [**AutoWire**](https://github.com/Swevo/AutoWire) | Compile-time DI auto-registration for `Microsoft.Extensions.DependencyInjection`. |
| [**AutoMap.Generator**](https://github.com/Swevo/AutoMap.Generator) | Compile-time object mapping with generated extension methods. |
| [**AutoValidate.Generator**](https://github.com/Swevo/AutoValidate.Generator) | Compile-time validator discovery and registration. |
| [**AutoResult.Generator**](https://github.com/Swevo/AutoResult.Generator) | Compile-time result helpers and `Try*()` wrappers. |
| [**AutoQuery.Generator**](https://github.com/Swevo/AutoQuery.Generator) | Compile-time query specifications for LINQ-based filtering. |

## License

MIT
