using System.Net.Http;
using System.Text;

namespace AutoHttpClient.OpenApiScaffold;

public static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!CommandLineOptions.TryParse(args, out var options, out var errorMessage, out var showHelp))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLineOptions.GetUsage());
            return 1;
        }

        if (showHelp || options is null)
        {
            Console.WriteLine(CommandLineOptions.GetUsage());
            return 0;
        }

        var input = await LoadInputAsync(options.Input).ConfigureAwait(false);
        if (!input.Success)
        {
            Console.Error.WriteLine(input.ErrorMessage);
            return 1;
        }

        var scaffolder = new OpenApiInterfaceScaffolder();
        var result = scaffolder.Scaffold(
            input.Content!,
            new ScaffoldOptions
            {
                InterfaceName = options.InterfaceName,
                Namespace = options.Namespace,
                SourceName = input.SourceName,
            });

        if (!result.Success)
        {
            Console.Error.WriteLine(result.ErrorMessage);
            return 1;
        }

        var outputPath = Path.GetFullPath(options.Output);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(outputPath, result.Output!, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);
        Console.WriteLine($"Generated {Path.GetFileName(outputPath)} from {input.SourceName}.");
        return 0;
    }

    private static async Task<InputLoadResult> LoadInputAsync(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                using var httpClient = new HttpClient();
                var content = await httpClient.GetStringAsync(uri).ConfigureAwait(false);
                return InputLoadResult.FromSuccess(content, uri.ToString());
            }
            catch (Exception ex)
            {
                return InputLoadResult.Failure($"Failed to download OpenAPI document from '{uri}': {ex.Message}");
            }
        }

        var fullPath = Path.GetFullPath(input);
        if (!File.Exists(fullPath))
        {
            return InputLoadResult.Failure($"Input file '{fullPath}' was not found.");
        }

        try
        {
            var content = await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);
            return InputLoadResult.FromSuccess(content, Path.GetFileName(fullPath));
        }
        catch (Exception ex)
        {
            return InputLoadResult.Failure($"Failed to read OpenAPI document '{fullPath}': {ex.Message}");
        }
    }

    private sealed class InputLoadResult
    {
        public string? Content { get; private init; }

        public string? ErrorMessage { get; private init; }

        public string SourceName { get; private init; } = string.Empty;

        public bool Success => ErrorMessage is null;

        public static InputLoadResult FromSuccess(string content, string sourceName)
            => new()
            {
                Content = content,
                SourceName = sourceName,
            };

        public static InputLoadResult Failure(string errorMessage)
            => new()
            {
                ErrorMessage = errorMessage,
            };
    }
}
