using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Hangfire;

/// <summary>Optional wiring for the Hangfire dashboard. Nothing in Emberdrop requires it.</summary>
public static class HangfireDashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Mounts the Hangfire dashboard and tells Emberdrop where it is, so UIs can link to it.
    /// </summary>
    /// <param name="app">The application pipeline.</param>
    /// <param name="path">Dashboard path.</param>
    /// <param name="options">Dashboard options, for example authorization filters. The default allows local requests only.</param>
    public static IApplicationBuilder UseEmberdropHangfireDashboard(
        this IApplicationBuilder app,
        string path = "/hangfire",
        DashboardOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var info = app.ApplicationServices.GetService<HangfireDashboardInfo>()
            ?? throw new InvalidOperationException("Hangfire is not configured. Call UseHangfire in AddEmberdrop first.");
        info.Path = path;

        return options is null ? app.UseHangfireDashboard(path) : app.UseHangfireDashboard(path, options);
    }
}
