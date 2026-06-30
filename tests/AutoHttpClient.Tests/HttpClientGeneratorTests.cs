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

    public static class HttpClientFactoryServiceCollectionExtensions
    {
        public static IServiceCollection AddHttpClient<TClient, TImplementation>(this IServiceCollection services) => services;
        public static IServiceCollection AddHttpClient<TClient, TImplementation>(this IServiceCollection services, System.Action<global::System.Net.Http.HttpClient> configureClient) => services;
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
        public void EnsureSuccessStatusCode() { }
    }

    public class HttpContent { }

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
}
";

    private const string HttpJsonStub = @"
namespace System.Net.Http.Json
{
    public static class HttpContentJsonExtensions
    {
        public static global::System.Threading.Tasks.Task<T?> ReadFromJsonAsync<T>(this global::System.Net.Http.HttpContent content, global::System.Text.Json.JsonSerializerOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(default(T));
    }

    public static class HttpClientJsonExtensions
    {
        public static global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> PostAsJsonAsync<T>(global::System.Net.Http.HttpClient client, string requestUri, T value, global::System.Text.Json.JsonSerializerOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new global::System.Net.Http.HttpResponseMessage());
        public static global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> PutAsJsonAsync<T>(global::System.Net.Http.HttpClient client, string requestUri, T value, global::System.Text.Json.JsonSerializerOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new global::System.Net.Http.HttpResponseMessage());
        public static global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> PatchAsJsonAsync<T>(global::System.Net.Http.HttpClient client, string requestUri, T value, global::System.Text.Json.JsonSerializerOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(new global::System.Net.Http.HttpResponseMessage());
    }

    public sealed class JsonContent : global::System.Net.Http.HttpContent
    {
        public static JsonContent Create<T>(T value, object? mediaType = null, global::System.Text.Json.JsonSerializerOptions? options = null) => new JsonContent();
    }
}
";

    private const string JsonStub = @"
namespace System.Text.Json
{
    public class JsonSerializerOptions
    {
        public static JsonSerializerOptions Web { get; } = new JsonSerializerOptions();
    }
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
        Assert.Contains("HttpClientJsonExtensions.PostAsJsonAsync(_httpClient, __url, request, _jsonOptions, ct)", source);
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
        Assert.Contains("AddHttpClient<global::IOrdersApi, global::OrdersApiClient>(services);", source);
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
}
