using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using Contione.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Contione.Logging.Tests;

public class HttpLoggingTests
{
    [Fact]
    public async Task Json_bodies_and_structured_properties_are_masked_and_rules_reload()
    {
        var sink = new CollectingSink();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["ContioneLogging:MaskFields:email"] = "Email";
        builder.Configuration["Serilog:MinimumLevel:Default"] = "Information";
        builder.AddContioneLogging();
        builder.Services.Configure<HttpLoggingOptions>(options =>
        {
            options.LoggingFields = HttpLoggingFields.All;
            options.CombineLogs = true;
        });
        builder.Services.AddSingleton<ILogEventSink>(sink);

        await using var app = builder.Build();
        app.UseContioneLogging();
        app.MapPost("/echo", (Contact contact, ILoggerFactory loggers) =>
        {
            loggers.CreateLogger("Test").LogInformation("Contact {@Contact}", contact);
            return Results.Ok(contact);
        });
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var first = await client.PostAsJsonAsync("/echo", new Contact("alice@example.com", "Alice"));
        first.EnsureSuccessStatusCode();

        AssertBody(sink.Events, "RequestBody", "a***@example.com");
        AssertBody(sink.Events, "ResponseBody", "a***@example.com");
        Assert.DoesNotContain("alice@example.com", string.Join('\n', sink.Events.Select(e => e.RenderMessage())));

        builder.Configuration["ContioneLogging:MaskFields:email"] = "[PRIVATE]";
        ((IConfigurationRoot)builder.Configuration).Reload();

        using var second = await client.PostAsJsonAsync("/echo", new Contact("bob@example.com", "Bob"));
        second.EnsureSuccessStatusCode();
        AssertBody(sink.Events, "ResponseBody", "[PRIVATE]");
        Assert.DoesNotContain("bob@example.com", string.Join('\n', sink.Events.Select(e => e.RenderMessage())));
    }

    [Fact]
    public async Task Nested_fields_and_invalid_or_oversized_bodies_do_not_leak()
    {
        var sink = new CollectingSink();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["ContioneLogging:MaskFields:email"] = "Email";
        builder.Configuration["ContioneLogging:BodyLogLimit"] = "80";
        builder.AddContioneLogging();
        builder.Services.AddSingleton<ILogEventSink>(sink);

        await using var app = builder.Build();
        app.UseContioneLogging();
        app.MapPost("/mirror", async (HttpContext context) =>
        {
            context.Response.ContentType = "application/json";
            await context.Request.Body.CopyToAsync(context.Response.Body);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();

        const string nested = "{\"items\":[{\"email\":\"alice@example.com\"}]}";
        using (var response = await client.PostAsync("/mirror", JsonContent(nested)))
            response.EnsureSuccessStatusCode();

        var body = Assert.IsType<StructureValue>(sink.Events.Last(e => e.Properties.ContainsKey("ResponseBody"))
            .Properties["ResponseBody"]);
        var items = Assert.IsType<SequenceValue>(body.Properties.Single(p => p.Name == "items").Value);
        var item = Assert.IsType<StructureValue>(items.Elements.Single());
        Assert.Equal("a***@example.com", Assert.IsType<ScalarValue>(item.Properties.Single().Value).Value);

        using (var response = await client.PostAsync("/mirror", JsonContent("{invalid")))
            response.EnsureSuccessStatusCode();
        Assert.Equal("[REDACTED]", Assert.IsType<ScalarValue>(sink.Events.Last(e => e.Properties.ContainsKey("ResponseBody"))
            .Properties["ResponseBody"]).Value);

        using (var response = await client.PostAsync("/mirror", JsonContent(
            "{\"email\":\"secret@example.com\",\"padding\":\"" + new string('x', 100) + "\"}")))
            response.EnsureSuccessStatusCode();
        Assert.Equal("[REDACTED]", Assert.IsType<ScalarValue>(sink.Events.Last(e => e.Properties.ContainsKey("RequestBody"))
            .Properties["RequestBody"]).Value);
        Assert.DoesNotContain("secret@example.com", string.Join('\n', sink.Events.Select(e => e.RenderMessage())));
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static void AssertBody(IEnumerable<LogEvent> events, string propertyName, string expectedEmail)
    {
        var body = Assert.IsType<StructureValue>(events.Last(e => e.Properties.ContainsKey(propertyName))
            .Properties[propertyName]);
        var email = body.Properties.Single(p => string.Equals(p.Name, "email", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectedEmail, Assert.IsType<ScalarValue>(email.Value).Value);
    }

    private sealed record Contact(string Email, string Name);

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();
        public IReadOnlyList<LogEvent> Events => _events.ToArray();
        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
