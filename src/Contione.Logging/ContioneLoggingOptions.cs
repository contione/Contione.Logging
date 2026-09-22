namespace Contione.Logging;

public sealed class ContioneLoggingOptions
{
    public const string SectionName = "ContioneLogging";

    public Dictionary<string, string> MaskFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int BodyLogLimit { get; set; } = 32_768;
}
