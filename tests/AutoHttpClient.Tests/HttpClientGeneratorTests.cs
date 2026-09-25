using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Xunit;

namespace AutoHttpClient.Tests;

public class HttpClientGeneratorTests
{
    private const string DependencyInjectionStub = @"
namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection { }

    public interface IHttpClientBuilder
    {
        IServiceCollection Services { get; }
    }

    public sealed class HttpClientBuilder : IHttpClientBuilder
    {
        public HttpClientBuilder(IServiceCollection services) => Services = services;
        public IServiceCollection Services { get; }
    }

    public static class HttpClientFactoryServiceCollectionExtensions
    {
        public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => new HttpClientBuilder(services);
        public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name, System.Action<global::System.Net.Http.HttpClient> configureClient) => new HttpClientBuilder(services);
    }

    public static class HttpClientBuilderExtensions
    {
        public static IHttpClientBuilder AddTypedClient<TClient>(this IHttpClientBuilder builder, System.Func<global::System.Net.Http.HttpClient, TClient> factory) where TClient : class => builder;
    }
}
";

    private const string HttpStub = @"
namespace System.Net.Http
{
    public class HttpClient
    {
        public global::System.Uri? BaseAddress { get; set; }
        public global::System.Threading.Tasks.Task<HttpResponseMessage> GetAsync(string requestUri, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage());
        public global::System.Threading.Tasks.Task<HttpResponseMessage> DeleteAsync(string requestUri, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage());
        public global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage());
    }

    public class HttpResponseMessage
    {
        public HttpContent Content { get; set; } = new HttpContent();
        public bool IsSuccessStatusCode { get; set; } = true;
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; } = ""OK"";
        public void EnsureSuccessStatusCode() { }
    }

    public class HttpContent
    {
        public global::System.Threading.Tasks.Task<string> ReadAsStringAsync(global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(string.Empty);
    }

    public class HttpRequestMessage : global::System.IDisposable
    {
        public HttpRequestMessage(HttpMethod method, string requestUri) { }
        public HttpContent? Content { get; set; }
        public HttpHeaders Headers { get; } = new HttpHeaders();
        public void Dispose() { }
    }

    public class HttpHeaders
    {
        public bool TryAddWithoutValidation(string name, string value) => true;
    }

    public class HttpMethod
    {
        private HttpMethod() { }
        public static HttpMethod Get { get; } = new HttpMethod();
        public static HttpMethod Post { get; } = new HttpMethod();
        public static HttpMethod Put { get; } = new HttpMethod();
        public static HttpMethod Delete { get; } = new HttpMethod();
        public static HttpMethod Patch { get; } = new HttpMethod();
    }

    public class MultipartFormDataContent : HttpContent
    {
        public void Add(HttpContent content, string name) { }
        public void Add(HttpContent content, string name, string fileName) { }
    }

    public class StringContent : HttpContent
    {
        public StringContent(string content) { }
    }

    public class ByteArrayContent : HttpContent
    {
        public ByteArrayContent(byte[] content) { }
    }

    public class StreamContent : HttpContent
    {
        public StreamContent(global::System.IO.Stream content) { }
    }
}
";

    private const string HttpJsonStub = @"
namespace System.Net.Http.Json
{
    public static class HttpContentJsonExtensions
    {
        public static global::System.Threading.Tasks.Task<T?> ReadFromJsonAsync<T>(this global::System.Net.Http.HttpContent content, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(default(T));
    }

    public static class HttpClientJsonExtensions
    {
        public static global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> PostAsJsonAsync<T>(global::System.Net.Http.HttpClient client, string requestUri, T value, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new global::System.Net.Http.HttpResponseMessage());
        public static global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> PutAsJsonAsync<T>(global::System.Net.Http.HttpClient client, string requestUri, T value, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new global::System.Net.Http.HttpResponseMessage());
        public static global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> PatchAsJsonAsync<T>(global::System.Net.Http.HttpClient client, string requestUri, T value, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new global::System.Net.Http.HttpResponseMessage());
    }

    public sealed class JsonContent : global::System.Net.Http.HttpContent
    {
        public static JsonContent Create<T>(T value, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo, object? mediaType = null) => new JsonContent();
    }
}
";

    private const string JsonStub = @"
namespace System.Text.Json
{
    public class JsonSerializerOptions
    {
        public static JsonSerializerOptions Web { get; } = new JsonSerializerOptions();
        public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo GetTypeInfo(global::System.Type type) => new global::System.Text.Json.Serialization.Metadata.JsonTypeInfo();
    }
}

namespace System.Text.Json.Serialization.Metadata
{
    public class JsonTypeInfo { }

    public class JsonTypeInfo<T> : JsonTypeInfo { }
}
";

    private const string UriStub = @"
namespace System
{
    public class Uri
    {
        public Uri(string value) { }
        public static string EscapeDataString(string value) => value;
    }
}
";

    private static Dictionary<string, string> RunGenerator(string userSource, out ImmutableArray<Diagnostic> diagnostics)
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        };

        try { refs.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location)); } catch { }
        try { refs.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)); } catch { }
        try { refs.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location)); } catch { }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(DependencyInjectionStub),
                CSharpSyntaxTree.ParseText(HttpStub),
                CSharpSyntaxTree.ParseText(HttpJsonStub),
                CSharpSyntaxTree.ParseText(JsonStub),
                CSharpSyntaxTree.ParseText(UriStub),
                CSharpSyntaxTree.ParseText(userSource),
            },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new AutoHttpClientGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out diagnostics);

        var compilationErrors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", System.StringComparison.Ordinal))
            .ToArray();

        Assert.True(compilationErrors.Length == 0, string.Join(System.Environment.NewLine, compilationErrors.Select(d => d.ToString())));

        return driver.GetRunResult().GeneratedTrees
            .ToDictionary(
                t => System.IO.Path.GetFileName(t.FilePath),
                t => t.GetText().ToString());
    }

    [Fact]
    public void Attributes_FileIsGenerated()
    {
        var sources = RunGenerator(string.Empty, out _);
        Assert.True(sources.ContainsKey("AutoHttpClient.Attributes.g.cs"));
    }

    [Fact]
    public void SingleGetMethod_GeneratesClientClass()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    Task<string> GetAsync(CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("internal sealed class OrdersApiClient : global::IOrdersApi", source);
        Assert.Contains("GetAsync", source);
    }

    [Fact]
    public void RouteParam_IsInterpolated()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders/{id}"")]
    Task<string> GetAsync(int id, CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("$\"/api/orders/{id}\"", source);
    }

    [Fact]
    public void QueryParam_AppendedToUrl()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    Task<string> GetAsync([Query(""status"")] string? status = null, CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("__query.Append(\"status\")", source);
        Assert.Contains("__url += \"?\" + __query.ToString();", source);
    }

    [Fact]
    public void BodyParam_SerializedAsJson()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

public sealed class CreateOrderRequest { }

[HttpClient]
public interface IOrdersApi
{
    [Post(""/api/orders"")]
    Task<string> CreateAsync([Body] CreateOrderRequest request, CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("HttpClientJsonExtensions.PostAsJsonAsync(_httpClient, __url, request, (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<global::CreateOrderRequest>)_jsonOptions.GetTypeInfo(typeof(global::CreateOrderRequest)), ct)", source);
    }

    [Fact]
    public void DI_RegistrationFileGenerated()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    Task<string> GetAsync(CancellationToken ct = default);
}", out _);

        var source = sources["AutoHttpClientRegistrations.g.cs"];
        Assert.Contains("AddAutoHttpClients", source);
        Assert.Contains("AddHttpClient(services, \"global::IOrdersApi\")", source);
        Assert.Contains("AddTypedClient<global::IOrdersApi>(", source);
        Assert.Contains("new global::OrdersApiClient(httpClient, jsonOptions)", source);
        Assert.Contains("RequiresUnreferencedCode", source);
        Assert.Contains("RequiresDynamicCode", source);
    }

    [Fact]
    public void AH001_NoHttpMethod_DiagnosticReported()
    {
        RunGenerator(@"
using AutoHttpClient;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    Task<string> GetAsync();
}", out var diagnostics);

        Assert.Contains(diagnostics, d => d.Id == "AH001" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void AH003_MultipleBody_DiagnosticReported()
    {
        RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

public sealed class A { }
public sealed class B { }

[HttpClient]
public interface IOrdersApi
{
    [Post(""/api/orders"")]
    Task<string> CreateAsync([Body] A a, [Body] B b, CancellationToken ct = default);
}", out var diagnostics);

        Assert.Contains(diagnostics, d => d.Id == "AH003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StaticHeaders_OnInterfaceAndMethod_AreEmitted()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
[Headers(""X-Api-Version: 1.0"")]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    [Headers(""Accept: application/json"")]
    Task<string> GetAsync(CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("__request.Headers.TryAddWithoutValidation(\"Accept\", \"application/json\");", source);
        Assert.Contains("__request.Headers.TryAddWithoutValidation(\"X-Api-Version\", \"1.0\");", source);
    }

    [Fact]
    public void HeaderCollection_ExpandsIntoRequestHeaders()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    Task<string> GetAsync([HeaderCollection] IDictionary<string, string?> headers, CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("foreach (var __entry_headers in headers)", source);
        Assert.Contains("__request.Headers.TryAddWithoutValidation(__entry_headers.Key, __entry_headers.Value.ToString()!);", source);
    }

    [Fact]
    public void QueryMap_ExpandsIntoQueryString()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    Task<string> GetAsync([QueryMap] IDictionary<string, string?> filters, CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("foreach (var __entry_filters in filters)", source);
        Assert.Contains("__query.Append(global::System.Uri.EscapeDataString(__entry_filters.Key)).Append(\"=\").Append(global::System.Uri.EscapeDataString(__entry_filters.Value.ToString()!));", source);
    }

    [Fact]
    public void Multipart_WithMixedPartTypes_GeneratesMultipartContent()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public sealed class Metadata { }

[HttpClient]
public interface IUploadsApi
{
    [Post(""/api/uploads"")]
    [Multipart]
    Task<string> UploadAsync(
        [Part(""file"", ""photo.png"")] Stream file,
        [Part(""description"")] string description,
        [Part] Metadata metadata,
        CancellationToken ct = default);
}", out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var source = sources["IUploadsApi.AutoHttpClient.g.cs"];
        Assert.Contains("var __multipart = new global::System.Net.Http.MultipartFormDataContent();", source);
        Assert.Contains("new global::System.Net.Http.StreamContent(file)", source);
        Assert.Contains("__multipart.Add(__part_file, \"file\", \"photo.png\");", source);
        Assert.Contains("new global::System.Net.Http.StringContent(description ?? string.Empty)", source);
        Assert.Contains("__multipart.Add(__part_description, \"description\");", source);
        Assert.Contains("global::System.Net.Http.Json.JsonContent.Create(metadata, (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<global::Metadata>)_jsonOptions.GetTypeInfo(typeof(global::Metadata)))", source);
        Assert.Contains("__request.Content = __multipart;", source);
    }

    [Fact]
    public void AH004_MultipartWithBody_DiagnosticReported()
    {
        RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

public sealed class Payload { }

[HttpClient]
public interface IUploadsApi
{
    [Post(""/api/uploads"")]
    [Multipart]
    Task<string> UploadAsync([Body] Payload payload, CancellationToken ct = default);
}", out var diagnostics);

        Assert.Contains(diagnostics, d => d.Id == "AH004" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AH005_PartWithoutMultipart_DiagnosticReported()
    {
        RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IUploadsApi
{
    [Post(""/api/uploads"")]
    Task<string> UploadAsync([Part] string description, CancellationToken ct = default);
}", out var diagnostics);

        Assert.Contains(diagnostics, d => d.Id == "AH005" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void CollectionQueryParam_ExpandsAsRepeatedEntries()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    Task<string> GetAsync([Query(""tag"")] List<string> tags, CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("foreach (var __item_tags in tags)", source);
        Assert.Contains("__query.Append(\"tag\").Append(\"=\").Append(global::System.Uri.EscapeDataString(__item_tags.ToString()!));", source);
    }

    [Fact]
    public void NonSuccessResponse_ThrowsApiException()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    Task<string> GetAsync(CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("await global::AutoHttpClient.AutoHttpClientResponseExtensions.EnsureSuccessAsync(__response, ct).ConfigureAwait(false);", source);

        var attributesSource = sources["AutoHttpClient.Attributes.g.cs"];
        Assert.Contains("public sealed class ApiException : Exception", attributesSource);
    }

    [Fact]
    public void Constructor_SplitsAotSafeAndFallbackOverloads()
    {
        var sources = RunGenerator(@"
using AutoHttpClient;
using System.Threading;
using System.Threading.Tasks;

[HttpClient]
public interface IOrdersApi
{
    [Get(""/api/orders"")]
    Task<string> GetAsync(CancellationToken ct = default);
}", out _);

        var source = sources["IOrdersApi.AutoHttpClient.g.cs"];
        Assert.Contains("public OrdersApiClient(global::System.Net.Http.HttpClient httpClient, global::System.Text.Json.JsonSerializerOptions jsonOptions)", source);
        Assert.Contains("_jsonOptions = jsonOptions;", source);
        Assert.Contains("RequiresUnreferencedCode", source);
        Assert.Contains("RequiresDynamicCode", source);
        Assert.Contains(": this(httpClient, global::System.Text.Json.JsonSerializerOptions.Web)", source);
    }
}
