using Contione.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.AddContioneLogging();

var app = builder.Build();
app.UseContioneLogging();

app.MapPost("/echo", (Contact contact, ILoggerFactory loggers) =>
{
    loggers.CreateLogger("Sample").LogInformation("Received {@Contact}", contact);
    return Results.Ok(contact);
});

app.Run();

record Contact(string Email, string Name);
