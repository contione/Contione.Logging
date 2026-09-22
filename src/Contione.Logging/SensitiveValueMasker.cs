namespace Contione.Logging;

internal static class SensitiveValueMasker
{
    internal const string Redacted = "[REDACTED]";

    internal static string Mask(string? value, string format)
    {
        if (string.IsNullOrEmpty(value))
            return Redacted;

        return format.ToUpperInvariant() switch
        {
            "EMAIL" => Email(value),
            "PHONE" => KeepEdges(value, 3, 4),
            "IDCARD" => KeepEdges(value, 6, 4),
            "BANKCARD" => KeepEnd(value, 4),
            "NAME" => value.Length == 1 ? "*" : $"{value[0]}{new string('*', value.Length - 1)}",
            "KEEPLAST4" => KeepEnd(value, 4),
            "FULL" => Redacted,
            _ => format
        };
    }

    private static string Email(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1
            ? $"{value[0]}***{value[at..]}"
            : Redacted;
    }

    private static string KeepEdges(string value, int start, int end) =>
        value.Length > start + end
            ? $"{value[..start]}{new string('*', value.Length - start - end)}{value[^end..]}"
            : Redacted;

    private static string KeepEnd(string value, int count) =>
        value.Length > count
            ? $"{new string('*', value.Length - count)}{value[^count..]}"
            : Redacted;
}
