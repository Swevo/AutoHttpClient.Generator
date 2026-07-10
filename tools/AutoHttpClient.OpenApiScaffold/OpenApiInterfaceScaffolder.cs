using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace AutoHttpClient.OpenApiScaffold;

public sealed class OpenApiInterfaceScaffolder
{
    private static readonly OperationType[] SupportedOperationOrder =
    [
        OperationType.Get,
        OperationType.Post,
        OperationType.Put,
        OperationType.Delete,
        OperationType.Patch,
    ];

    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];

    public ScaffoldResult Scaffold(string documentText, ScaffoldOptions options)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            return ScaffoldResult.FromError("The supplied OpenAPI document is empty.");
        }

        OpenApiDocument? document;
        OpenApiDiagnostic diagnostic;

        try
        {
            var reader = new OpenApiStringReader();
            document = reader.Read(documentText, out diagnostic);
        }
        catch (Exception ex)
        {
            return ScaffoldResult.FromError($"Failed to parse OpenAPI document: {ex.Message}");
        }

        if (document is null)
        {
            return ScaffoldResult.FromError("Failed to parse OpenAPI document.");
        }

        if (diagnostic.Errors.Count > 0)
        {
            var message = string.Join("; ", diagnostic.Errors.Select(static error => error.ToString()));
            return ScaffoldResult.FromError($"Failed to parse OpenAPI document: {message}");
        }

        var interfaceName = ResolveInterfaceName(document, options);
        var operations = BuildOperations(document);
        if (operations.Count == 0)
        {
            return ScaffoldResult.FromError("The OpenAPI document does not contain any supported GET/POST/PUT/DELETE/PATCH operations.");
        }

        var output = BuildOutput(document, interfaceName, options.Namespace, operations);
        return ScaffoldResult.FromOutput(output);
    }

    private static List<ScaffoldedOperation> BuildOperations(OpenApiDocument document)
    {
        var operations = new List<ScaffoldedOperation>();

        foreach (var pathEntry in document.Paths.OrderBy(static path => path.Key, StringComparer.Ordinal))
        {
            foreach (var operationType in SupportedOperationOrder)
            {
                if (!pathEntry.Value.Operations.TryGetValue(operationType, out var operation))
                {
                    continue;
                }

                var parameters = CollectParameters(pathEntry.Value, operation);
                var routeParametersByName = parameters
                    .Where(static parameter => parameter.In == ParameterLocation.Path)
                    .ToDictionary(static parameter => parameter.Name, static parameter => parameter, StringComparer.OrdinalIgnoreCase);

                var usedNames = new HashSet<string>(StringComparer.Ordinal);
                var routeParameterInfos = new List<ScaffoldedParameter>();
                var routeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var routeToken in ParseRouteTokens(pathEntry.Key))
                {
                    if (!routeParametersByName.TryGetValue(routeToken, out var parameter))
                    {
                        continue;
                    }

                    var parameterInfo = CreateParameter(parameter, usedNames);
                    routeParameterInfos.Add(parameterInfo with { AttributeText = null });
                    routeNameMap[routeToken] = parameterInfo.SymbolName;
                }

                var queryParameterInfos = parameters
                    .Where(static parameter => parameter.In == ParameterLocation.Query)
                    .Select(parameter => CreateParameter(parameter, usedNames) with { AttributeText = $"[Query(\"{EscapeString(parameter.Name)}\")]" })
                    .ToList();

                var headerParameterInfos = parameters
                    .Where(static parameter => parameter.In == ParameterLocation.Header)
                    .Select(parameter => CreateParameter(parameter, usedNames) with { AttributeText = $"[Header(\"{EscapeString(parameter.Name)}\")]" })
                    .ToList();

                var requestBodyParameter = CreateRequestBodyParameter(operation.RequestBody, usedNames);
                var allParameters = new List<ScaffoldedParameter>();
                allParameters.AddRange(routeParameterInfos);
                allParameters.AddRange(queryParameterInfos);
                allParameters.AddRange(headerParameterInfos);
                if (requestBodyParameter is not null)
                {
                    allParameters.Add(requestBodyParameter);
                }

                allParameters.Add(new ScaffoldedParameter(
                    SourceName: "ct",
                    SymbolName: "ct",
                    DeclarationName: "ct",
                    TypeName: "CancellationToken",
                    AttributeText: null,
                    IsOptional: true));

                var returnType = ResolveReturnType(operation);
                operations.Add(new ScaffoldedOperation(
                    MethodName: ResolveMethodName(operationType, pathEntry.Key, operation),
                    AttributeName: operationType.ToString(),
                    RouteTemplate: RewriteRouteTemplate(pathEntry.Key, routeNameMap),
                    ReturnType: returnType,
                    Parameters: allParameters));
            }
        }

        return operations;
    }

    private static IReadOnlyList<OpenApiParameter> CollectParameters(OpenApiPathItem pathItem, OpenApiOperation operation)
    {
        var combined = new Dictionary<string, OpenApiParameter>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in pathItem.Parameters)
        {
            combined[$"{parameter.In}:{parameter.Name}"] = parameter;
        }

        foreach (var parameter in operation.Parameters)
        {
            combined[$"{parameter.In}:{parameter.Name}"] = parameter;
        }

        return combined.Values
            .OrderBy(static parameter => parameter.In)
            .ThenBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ScaffoldedParameter CreateParameter(OpenApiParameter parameter, HashSet<string> usedNames)
    {
        var resolvedType = ResolveSchemaType(parameter.Schema, parameter.Required);
        var sourceName = CreateUniqueIdentifier(parameter.Name, usedNames, forType: false, fallbackName: "value");
        return new ScaffoldedParameter(
            SourceName: parameter.Name,
            SymbolName: UnescapeIdentifier(sourceName),
            DeclarationName: sourceName,
            TypeName: resolvedType,
            AttributeText: null,
            IsOptional: !parameter.Required);
    }

    private static ScaffoldedParameter? CreateRequestBodyParameter(OpenApiRequestBody? requestBody, HashSet<string> usedNames)
    {
        if (requestBody is null || requestBody.Content.Count == 0)
        {
            return null;
        }

        var content = SelectContent(requestBody.Content);
        if (content?.Schema is null)
        {
            return null;
        }

        var parameterType = ResolveSchemaType(content.Schema, requestBody.Required);
        var baseName = GetSchemaName(content.Schema) is { Length: > 0 } schemaName
            ? ToCamelCase(schemaName)
            : "request";
        var declarationName = CreateUniqueIdentifier(baseName, usedNames, forType: false, fallbackName: "request");

        return new ScaffoldedParameter(
            SourceName: baseName,
            SymbolName: UnescapeIdentifier(declarationName),
            DeclarationName: declarationName,
            TypeName: parameterType,
            AttributeText: "[Body]",
            IsOptional: !requestBody.Required);
    }

    private static string ResolveReturnType(OpenApiOperation operation)
    {
        var successResponse = SelectSuccessResponse(operation.Responses);
        if (successResponse is null)
        {
            return "Task";
        }

        var content = SelectContent(successResponse.Content);
        if (content?.Schema is null)
        {
            return "Task";
        }

        return $"Task<{ResolveSchemaType(content.Schema, required: true)}>";
    }

    private static OpenApiResponse? SelectSuccessResponse(OpenApiResponses responses)
    {
        foreach (var preferredCode in new[] { "200", "201" })
        {
            if (responses.TryGetValue(preferredCode, out var preferred))
            {
                return preferred;
            }
        }

        return responses
            .Where(static response => response.Key.Length == 3 && response.Key[0] == '2')
            .OrderBy(static response => response.Key, StringComparer.Ordinal)
            .Select(static response => response.Value)
            .FirstOrDefault();
    }

    private static OpenApiMediaType? SelectContent(IDictionary<string, OpenApiMediaType> content)
        => content
            .OrderByDescending(static item => IsJsonMediaType(item.Key))
            .ThenBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Value)
            .FirstOrDefault();

    private static bool IsJsonMediaType(string mediaType)
        => mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
           || mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
           || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    private static string ResolveSchemaType(OpenApiSchema? schema, bool required)
    {
        if (schema is null)
        {
            return MakeNullable("JsonElement", isValueType: true, shouldBeNullable: !required);
        }

        if (schema.Reference?.Id is { Length: > 0 } referenceId)
        {
            return MakeNullable(SanitizeTypeName(referenceId, "AnonymousDto"), isValueType: false, shouldBeNullable: !required || schema.Nullable);
        }

        if (schema.Type == "array")
        {
            var itemType = ResolveSchemaType(schema.Items, required: true).TrimEnd('?');
            return MakeNullable($"List<{itemType}>", isValueType: false, shouldBeNullable: !required || schema.Nullable);
        }

        if (schema.AdditionalProperties is not null)
        {
            var valueType = ResolveSchemaType(schema.AdditionalProperties, required: true).TrimEnd('?');
            return MakeNullable($"Dictionary<string, {valueType}>", isValueType: false, shouldBeNullable: !required || schema.Nullable);
        }

        var typeName = schema.Type switch
        {
            "string" => schema.Format switch
            {
                "date" or "date-time" => "DateTimeOffset",
                "uuid" => "Guid",
                "byte" or "binary" => "byte[]",
                _ => "string",
            },
            "integer" => schema.Format switch
            {
                "int64" => "long",
                _ => "int",
            },
            "number" => schema.Format switch
            {
                "float" => "float",
                "decimal" => "decimal",
                _ => "double",
            },
            "boolean" => "bool",
            "object" when schema.Properties.Count == 0 && schema.AdditionalPropertiesAllowed == true && schema.AdditionalProperties is null => "Dictionary<string, JsonElement>",
            "object" => "JsonElement",
            _ when schema.Enum.Count > 0 => schema.Type == "integer" ? "int" : "string",
            _ => "JsonElement",
        };

        var isValueType = typeName is "int" or "long" or "float" or "double" or "decimal" or "bool" or "DateTimeOffset" or "Guid" or "JsonElement";
        return MakeNullable(typeName, isValueType, shouldBeNullable: !required || schema.Nullable);
    }

    private static string BuildOutput(
        OpenApiDocument document,
        string interfaceName,
        string? @namespace,
        IReadOnlyList<ScaffoldedOperation> operations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by AutoHttpClient.OpenApiScaffold/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using AutoHttpClient;");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(@namespace))
        {
            sb.Append("namespace ").Append(@namespace).AppendLine();
            sb.AppendLine("{");
            sb.AppendLine();
        }

        var indent = string.IsNullOrWhiteSpace(@namespace) ? string.Empty : "    ";
        var baseAddress = ResolveBaseAddress(document);
        if (!string.IsNullOrWhiteSpace(baseAddress))
        {
            sb.Append(indent)
                .Append("[HttpClient(BaseAddress = \"")
                .Append(EscapeString(baseAddress))
                .AppendLine("\")]");
        }
        else
        {
            sb.Append(indent).AppendLine("[HttpClient]");
        }

        sb.Append(indent).Append("public partial interface ").Append(interfaceName).AppendLine();
        sb.Append(indent).AppendLine("{");

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            sb.Append(indent)
                .Append("    [")
                .Append(operation.AttributeName)
                .Append("(\"")
                .Append(EscapeString(operation.RouteTemplate))
                .AppendLine("\")]");
            sb.Append(indent)
                .Append("    ")
                .Append(operation.ReturnType)
                .Append(' ')
                .Append(operation.MethodName)
                .Append('(')
                .Append(string.Join(", ", operation.Parameters.Select(FormatParameter)))
                .AppendLine(");");

            if (index < operations.Count - 1)
            {
                sb.AppendLine();
            }
        }

        sb.Append(indent).AppendLine("}");

        if (!string.IsNullOrWhiteSpace(@namespace))
        {
            sb.AppendLine();
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string FormatParameter(ScaffoldedParameter parameter)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(parameter.AttributeText))
        {
            builder.Append(parameter.AttributeText).Append(' ');
        }

        builder.Append(parameter.TypeName)
            .Append(' ')
            .Append(parameter.DeclarationName);

        if (parameter.IsOptional)
        {
            builder.Append(" = default");
        }

        return builder.ToString();
    }

    private static string? ResolveBaseAddress(OpenApiDocument document)
    {
        var serverUrl = document.Servers.FirstOrDefault()?.Url;
        if (string.IsNullOrWhiteSpace(serverUrl) || serverUrl.Contains('{', StringComparison.Ordinal))
        {
            return null;
        }

        return serverUrl;
    }

    private static string ResolveInterfaceName(OpenApiDocument document, ScaffoldOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.InterfaceName))
        {
            return SanitizeTypeName(options.InterfaceName, "IApiClient");
        }

        var source = document.Info.Title;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = options.SourceName;
        }

        var name = SanitizeTypeName(source, "ApiClient");
        if (!name.StartsWith("I", StringComparison.Ordinal) || name.Length == 1 || !char.IsUpper(name[1]))
        {
            name = $"I{name}";
        }

        if (!name.EndsWith("Api", StringComparison.Ordinal) && !name.EndsWith("Client", StringComparison.Ordinal))
        {
            name += "Api";
        }

        return name;
    }

    private static string ResolveMethodName(OperationType operationType, string path, OpenApiOperation operation)
    {
        var candidate = !string.IsNullOrWhiteSpace(operation.OperationId)
            ? SanitizeTypeName(operation.OperationId, $"{operationType}Request")
            : $"{operationType}{BuildPathName(path)}";

        if (!candidate.EndsWith("Async", StringComparison.Ordinal))
        {
            candidate += "Async";
        }

        return candidate;
    }

    private static string BuildPathName(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            if (part.StartsWith('{') && part.EndsWith('}'))
            {
                builder.Append("By").Append(SanitizeTypeName(part.Trim('{', '}'), "Value"));
            }
            else
            {
                builder.Append(SanitizeTypeName(part, string.Empty));
            }
        }

        return builder.Length == 0 ? "Root" : builder.ToString();
    }

    private static IReadOnlyList<string> ParseRouteTokens(string template)
    {
        var tokens = new List<string>();
        var matches = Regex.Matches(template, @"\{([^}]+)\}");
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                tokens.Add(match.Groups[1].Value);
            }
        }

        return tokens;
    }

    private static string RewriteRouteTemplate(string template, IReadOnlyDictionary<string, string> routeNameMap)
        => Regex.Replace(
            template,
            @"\{([^}]+)\}",
            match =>
            {
                var originalName = match.Groups[1].Value;
                return routeNameMap.TryGetValue(originalName, out var rewrittenName)
                    ? "{" + rewrittenName + "}"
                    : match.Value;
            });

    private static string? GetSchemaName(OpenApiSchema schema)
        => schema.Reference?.Id
           ?? schema.Title
           ?? (schema.Extensions.TryGetValue("x-schema-name", out var extension) && extension is OpenApiString schemaNameExtension
                ? schemaNameExtension.Value
                : null);

    private static string CreateUniqueIdentifier(string value, HashSet<string> usedNames, bool forType, string fallbackName)
    {
        var candidate = SanitizeIdentifier(value, fallbackName, forType);
        var baseName = candidate;
        var counter = 2;
        while (!usedNames.Add(UnescapeIdentifier(candidate)))
        {
            candidate = $"{baseName}{counter.ToString(CultureInfo.InvariantCulture)}";
            counter++;
        }

        return EscapeKeyword(candidate);
    }

    private static string SanitizeTypeName(string? value, string fallbackName)
        => SanitizeIdentifier(value, fallbackName, forType: true);

    private static string SanitizeIdentifier(string? value, string fallbackName, bool forType)
    {
        var words = Regex.Matches(value ?? string.Empty, @"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")
            .Select(static match => match.Value)
            .ToArray();

        if (words.Length == 0)
        {
            words = Regex.Matches(value ?? string.Empty, @"[A-Za-z0-9]+")
                .Select(static match => match.Value)
                .ToArray();
        }

        if (words.Length == 0)
        {
            words = [fallbackName];
        }

        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        var builder = new StringBuilder();
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            if (word.Length == 0)
            {
                continue;
            }

            if (!forType && index == 0)
            {
                builder.Append(char.ToLowerInvariant(word[0]));
                if (word.Length > 1)
                {
                    builder.Append(word.Substring(1));
                }
            }
            else
            {
                builder.Append(textInfo.ToTitleCase(word.ToLowerInvariant()));
            }
        }

        if (builder.Length == 0)
        {
            builder.Append(forType ? fallbackName : char.ToLowerInvariant(fallbackName[0]) + fallbackName.Substring(1));
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, forType ? 'T' : '_');
        }

        return builder.ToString();
    }

    private static string ToCamelCase(string value)
        => string.IsNullOrEmpty(value)
            ? value
            : char.ToLowerInvariant(value[0]) + value.Substring(1);

    private static string EscapeKeyword(string identifier)
        => CSharpKeywords.Contains(identifier) ? "@" + identifier : identifier;

    private static string UnescapeIdentifier(string identifier)
        => identifier.StartsWith("@", StringComparison.Ordinal) ? identifier.Substring(1) : identifier;

    private static string MakeNullable(string typeName, bool isValueType, bool shouldBeNullable)
    {
        if (!shouldBeNullable || typeName.EndsWith("?", StringComparison.Ordinal))
        {
            return typeName;
        }

        return isValueType || typeName.EndsWith("[]", StringComparison.Ordinal)
            ? typeName + "?"
            : typeName + "?";
    }

    private static string EscapeString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record ScaffoldedOperation(
        string MethodName,
        string AttributeName,
        string RouteTemplate,
        string ReturnType,
        IReadOnlyList<ScaffoldedParameter> Parameters);

    private sealed record ScaffoldedParameter(
        string SourceName,
        string SymbolName,
        string DeclarationName,
        string TypeName,
        string? AttributeText,
        bool IsOptional);
}
