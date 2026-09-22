# Contione.Logging

ASP.NET Core HTTP logging with Serilog and live-configured sensitive-field masking. Targets .NET 10.

`Microsoft.AspNetCore.HttpLogging` captures request and response metadata. A bounded body capture layer sends JSON bodies as named properties to Serilog, where `SensitiveDataEnricher` turns them into structured values and masks configured fields. Field names match at any depth, ignoring case. The built-in HttpLogging body output is disabled because it embeds raw content directly in message text.

```csharp
using Contione.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.AddContioneLogging();

var app = builder.Build();
app.UseContioneLogging();
app.MapPost("/echo", (Contact contact) => Results.Ok(contact));
app.Run();

record Contact(string Email, string Name);
```

Configure a Serilog sink and mask rules in `appsettings.json`:

Install the sink package you choose (for the example below, `Serilog.Sinks.Console`) in the host application.

```json
{
  "ContioneLogging": {
    "MaskFields": {
      "email": "Email",
      "password": "[REDACTED]"
    }
  },
  "Serilog": {
    "WriteTo": [ { "Name": "Console" } ]
  }
}
```

`Email` keeps the first character of the local part and the domain, for example `alice@example.com` becomes `a***@example.com`. Any other value is used as the literal replacement. Changes to the configuration file take effect through `IOptionsMonitor` without restarting the app, provided the host loads it with `reloadOnChange` (the default for `WebApplication.CreateBuilder`).

HTTP JSON bodies become nested Serilog objects, including arrays. Invalid or truncated JSON bodies are logged as `[REDACTED]`. Non-JSON bodies are not logged. `BodyLogLimit` defaults to 32768 bytes and can be configured under `ContioneLogging` (clamped to 1 MiB). Masking works on named structured properties; it cannot identify personal data embedded in arbitrary message text. Configure sinks and retention through Serilog as usual. You can adjust other `HttpLoggingOptions`, but the package always disables its raw body fields and combined logs to prevent an unmasked copy.

Run the sample with `dotnet run --project samples/Contione.Logging.Sample` and send a JSON POST to `/echo`.
