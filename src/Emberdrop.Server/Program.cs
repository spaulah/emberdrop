using Emberdrop.Hangfire;
using Emberdrop.Storage;
using Emberdrop.WorkUnits;
using Emberdrop.Server.WorkUnitManagement;
using Emberdrop.Server.Extensions;
using Emberdrop.Server.Plugins;
using Serilog;

// Configure Serilog early
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .Build())
    .CreateLogger();

try
{
    Log.Information("Starting Emberdrop Server");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog for logging
    builder.Host.UseSerilog();

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    builder.Services.AddEmberdropProviders(builder.Configuration);

    builder.Services.AddWorkUnitManager();

    // Add HttpClient for dependency injection
    builder.Services.AddHttpClient();

    builder.Services.AddPluginServices(builder.Configuration);

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    if (ProviderSelection.From(app.Configuration).UsesHangfire)
    {
        // The dashboard belongs to the Hangfire engine; other engines have none.
        app.UseEmberdropHangfireDashboard();
    }

    using (var scope = app.Services.CreateScope())
    {
        foreach (var migrator in scope.ServiceProvider.GetServices<IStoreMigrator>())
        {
            await migrator.MigrateAsync();
        }

        var workUnitManager = scope.ServiceProvider.GetRequiredService<WorkUnitManager>();
        await workUnitManager.InitializeWorkUnitsAsync();

        var pluginService = scope.ServiceProvider.GetRequiredService<PluginService>();
        await pluginService.StartMonitoringAsync();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Emberdrop Server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
