using System.Text.Json;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;

namespace Contione.Logging;

public sealed class SensitiveDataEnricher(IOptionsMonitor<ContioneLoggingOptions> options) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var fields = options.CurrentValue.MaskFields;
        foreach (var property in logEvent.Properties.ToArray())
        {
            var value = Rewrite(property.Key, property.Value, fields);
            if (!ReferenceEquals(value, property.Value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, value));
        }
    }

    private static LogEventPropertyValue Rewrite(
        string? name,
        LogEventPropertyValue value,
        IReadOnlyDictionary<string, string> fields)
    {
        if (name is not null && TryGetFormat(fields, name, out var format))
            return new ScalarValue(SensitiveValueMasker.Mask((value as ScalarValue)?.Value?.ToString(), format));

        if (name is "RequestBody" or "ResponseBody" && value is ScalarValue { Value: string body })
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                value = FromJson(document.RootElement);
            }
            catch (JsonException)
            {
                return new ScalarValue(SensitiveValueMasker.Redacted);
            }
        }

        return value switch
        {
            StructureValue structure => new StructureValue(
                structure.Properties.Select(p => new LogEventProperty(p.Name, Rewrite(p.Name, p.Value, fields))),
                structure.TypeTag),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(v => Rewrite(null, v, fields))),
            DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.Select(p =>
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(p.Key, Rewrite(p.Key.Value?.ToString(), p.Value, fields)))),
            _ => value
        };
    }

    private static bool TryGetFormat(IReadOnlyDictionary<string, string> fields, string name, out string format)
    {
        if (fields.TryGetValue(name, out format!))
            return true;

        foreach (var field in fields)
        {
            if (string.Equals(field.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                format = field.Value;
                return true;
            }
        }

        format = string.Empty;
        return false;
    }

    private static LogEventPropertyValue FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new StructureValue(element.EnumerateObject()
            .Select(p => new LogEventProperty(p.Name, FromJson(p.Value)))),
        JsonValueKind.Array => new SequenceValue(element.EnumerateArray().Select(FromJson)),
        JsonValueKind.String => new ScalarValue(element.GetString()),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => new ScalarValue(integer),
        JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => new ScalarValue(decimalValue),
        JsonValueKind.Number => new ScalarValue(element.GetDouble()),
        JsonValueKind.True => new ScalarValue(true),
        JsonValueKind.False => new ScalarValue(false),
        _ => new ScalarValue(null)
    };
}
