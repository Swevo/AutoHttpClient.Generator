namespace AutoHttpClient.OpenApiScaffold;

public sealed class ScaffoldResult
{
    public bool Success => ErrorMessage is null;

    public string? Output { get; init; }

    public string? ErrorMessage { get; init; }

    public static ScaffoldResult FromOutput(string output)
        => new()
        {
            Output = output,
        };

    public static ScaffoldResult FromError(string errorMessage)
        => new()
        {
            ErrorMessage = errorMessage,
        };
}
