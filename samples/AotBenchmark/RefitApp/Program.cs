using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Refit;

var sw = Stopwatch.StartNew();

using var httpClient = new HttpClient { BaseAddress = new Uri("https://example.invalid") };
IPingApi client = RestService.ForGenerated<IPingApi>(httpClient, SampleJsonContext.Default);

sw.Stop();
Console.WriteLine($"Refit setup time: {sw.Elapsed.TotalMilliseconds:F3} ms");
Console.WriteLine($"Generated client type: {client.GetType().FullName}");

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
