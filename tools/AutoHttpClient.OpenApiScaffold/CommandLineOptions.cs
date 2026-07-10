namespace AutoHttpClient.OpenApiScaffold;

public sealed class CommandLineOptions
{
    public string Input { get; private init; } = string.Empty;

    public string Output { get; private init; } = string.Empty;

    public string? Namespace { get; private init; }

    public string? InterfaceName { get; private init; }

    public static string GetUsage()
        => """
           Usage:
             dotnet run --project tools\AutoHttpClient.OpenApiScaffold -- --input <swagger.json|url> --output <IMyApiClient.g.cs> [--namespace <MyApp.Clients>] [--interface-name <IMyApiClient>]

           Options:
             --input           Path or URL to an OpenAPI/Swagger JSON document.
             --output          Destination .cs file for the scaffolded interface.
             --namespace       Optional namespace for the generated interface.
             --interface-name  Optional interface name. Defaults to one derived from the spec title or input file name.
             --help            Show this help text.
           """;

    public static bool TryParse(
        IReadOnlyList<string> args,
        out CommandLineOptions? options,
        out string? errorMessage,
        out bool showHelp)
    {
        options = null;
        errorMessage = null;
        showHelp = false;

        if (args.Count == 0)
        {
            errorMessage = "Missing required arguments.";
            return false;
        }

        string? input = null;
        string? output = null;
        string? @namespace = null;
        string? interfaceName = null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    options = new CommandLineOptions();
                    return true;
                case "--input":
                    if (!TryReadValue(args, ref index, out input, out errorMessage))
                    {
                        return false;
                    }

                    break;
                case "--output":
                    if (!TryReadValue(args, ref index, out output, out errorMessage))
                    {
                        return false;
                    }

                    break;
                case "--namespace":
                    if (!TryReadValue(args, ref index, out @namespace, out errorMessage))
                    {
                        return false;
                    }

                    break;
                case "--interface-name":
                    if (!TryReadValue(args, ref index, out interfaceName, out errorMessage))
                    {
                        return false;
                    }

                    break;
                default:
                    errorMessage = $"Unknown argument '{arg}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Missing required --input value.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            errorMessage = "Missing required --output value.";
            return false;
        }

        options = new CommandLineOptions
        {
            Input = input,
            Output = output,
            Namespace = @namespace,
            InterfaceName = interfaceName,
        };
        return true;
    }

    private static bool TryReadValue(IReadOnlyList<string> args, ref int index, out string? value, out string? errorMessage)
    {
        if (index + 1 >= args.Count)
        {
            value = null;
            errorMessage = $"Missing value for '{args[index]}'.";
            return false;
        }

        value = args[++index];
        errorMessage = null;
        return true;
    }
}
