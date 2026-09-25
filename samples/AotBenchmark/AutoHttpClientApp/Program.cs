using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoHttpClient;

var sw = Stopwatch.StartNew();

var jsonOptions = new JsonSerializerOptions(SampleJsonContext.Default.Options)
{
    TypeInfoResolver = SampleJsonContext.Default,
};

using var httpClient = new HttpClient { BaseAddress = new Uri("https://example.invalid") };
IPingApi client = new PingApiClient(httpClient, jsonOptions);

sw.Stop();
Console.WriteLine($"AutoHttpClient setup time: {sw.Elapsed.TotalMilliseconds:F3} ms");
Console.WriteLine($"Generated client type: {client.GetType().FullName}");

[HttpClient]
public interface IPingApi
{
    [Get("/ping")]
    Task<PingResult> PingAsync(CancellationToken ct = default);
}

public sealed class PingResult
{
    public string Message { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PingResult))]
internal sealed partial class SampleJsonContext : JsonSerializerContext
{
}
