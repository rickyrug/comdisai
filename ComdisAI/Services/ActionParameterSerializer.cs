using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace ComdisAI.Services;

public static class ActionParameterSerializer
{
    public const int MaximumLength = 4_000;
    private const string RedactedValue = "[REDACTED]";
    private const string TruncatedSuffix = "...[truncated]";

    private static readonly string[] SensitivePropertyNames =
    [
        "password",
        "passwd",
        "token",
        "secret",
        "credential",
        "authorization",
        "cookie",
        "email",
        "phone",
        "mobile",
        "address",
        "name",
        "firstname",
        "lastname",
        "surname",
        "birthdate",
        "dateofbirth",
        "ssn",
        "socialsecurity"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 6,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public static string Serialize(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        var argumentDictionary = arguments
            .Where(argument => argument.Value is not CancellationToken)
            .ToDictionary(
                argument => argument.Key,
                argument => NormalizeValue(argument.Value),
                StringComparer.Ordinal);
        var json = JsonSerializer.SerializeToNode(argumentDictionary, SerializerOptions);
        RedactSensitiveValues(json);

        var serialized = json?.ToJsonString() ?? "{}";
        if (serialized.Length <= MaximumLength)
            return serialized;

        return serialized[..(MaximumLength - TruncatedSuffix.Length)] + TruncatedSuffix;
    }

    private static void RedactSensitiveValues(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (IsSensitiveProperty(property.Key))
                    jsonObject[property.Key] = RedactedValue;
                else
                    RedactSensitiveValues(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var value in jsonArray)
                RedactSensitiveValues(value);
        }
    }

    private static bool IsSensitiveProperty(string propertyName) =>
        SensitivePropertyNames.Any(
            sensitiveName => propertyName.Contains(sensitiveName, StringComparison.OrdinalIgnoreCase));

    private static object? NormalizeValue(object? value) =>
        value switch
        {
            IFormFile or IFormFileCollection => "[file content omitted]",
            HttpContext or HttpRequest or HttpResponse => "[framework value omitted]",
            _ => value
        };
}
