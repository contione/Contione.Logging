using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Contione.Logging;

public static class ContioneLoggingExtensions
{
    public static WebApplicationBuilder AddContioneLogging(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ContioneLoggingOptions>(
            builder.Configuration.GetSection(ContioneLoggingOptions.SectionName));
        builder.Services.AddSingleton<SensitiveDataEnricher>();
        builder.Services.AddSingleton<Serilog.Core.ILogEventEnricher>(services =>
            services.GetRequiredService<SensitiveDataEnricher>());

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

        builder.Services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.All;
        });
        builder.Services.PostConfigure<HttpLoggingOptions>(options =>
        {
            options.LoggingFields &= ~HttpLoggingFields.RequestBody & ~HttpLoggingFields.ResponseBody;
            options.CombineLogs = false;
        });

        return builder;
    }

    public static WebApplication UseContioneLogging(this WebApplication app)
    {
        app.UseHttpLogging();
        app.UseMiddleware<HttpBodyLoggingMiddleware>();
        return app;
    }
}
