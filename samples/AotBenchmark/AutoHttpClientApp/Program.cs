using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoHttpClient;
using Microsoft.Extensions.DependencyInjection;

var sw = Stopwatch.StartNew();

var jsonOptions = new JsonSerializerOptions(SampleJsonContext.Default.Options)
{
    TypeInfoResolver = SampleJsonContext.Default,
};

using var httpClient = new HttpClient { BaseAddress = new Uri("https://example.invalid") };
IPingApi client = new PingApiClient(httpClient, jsonOptions);

// Also exercise the DI registration path (AddAutoHttpClients) with the safe, explicit-options
// overload to prove it is warning-free too, not just direct `new` construction.
var services = new ServiceCollection();
services.AddAutoHttpClients(jsonOptions);
using var provider = services.BuildServiceProvider();
IPingApi diClient = provider.GetRequiredService<IPingApi>();

sw.Stop();
Console.WriteLine($"AutoHttpClient setup time: {sw.Elapsed.TotalMilliseconds:F3} ms");
Console.WriteLine($"Generated client type: {client.GetType().FullName}");
Console.WriteLine($"DI-resolved client type: {diClient.GetType().FullName}");

[HttpClient]
public interface IPingApi
{
    [Get("/ping")]
    Task<PingResult> PingAsync(CancellationToken ct = default);

    [Post("/ping")]
    Task<PingResult> PingWithBodyAsync([Body] PingRequest request, CancellationToken ct = default);
}

public sealed class PingResult
{
    public string Message { get; set; } = string.Empty;
}

public sealed class PingRequest
{
    public string Message { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(PingRequest))]
internal sealed partial class SampleJsonContext : JsonSerializerContext
{
}
