using FluentAssertions;
using Emberdrop.Hangfire;
using Emberdrop.Hosting;
using Emberdrop.InMemory;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Emberdrop.Server.Infrastructure;
using Emberdrop.Server.WorkUnitManagement;
using Emberdrop.Storage;
using global::Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Emberdrop.Server.Tests.WorkUnits;

public class WorkUnitManagerRegistrationShould
{
    [Fact]
    public void WireTheCompleteWorkUnitManagementGraph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddWorkUnitManager();
        // Hangfire needs a storage; this test only checks the wiring, so use a stub.
        services.AddEmberdrop(emberdrop => emberdrop
            .UseHangfire(hangfire => hangfire.UseStorage(new Moq.Mock<JobStorage>().Object))
            .UseInMemoryStore());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<WorkUnitManager>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IWorkStore>().Should().BeOfType<Emberdrop.InMemory.InMemoryWorkStore>();
        scope.ServiceProvider.GetRequiredService<IWorkScheduler>().Should().BeOfType<Emberdrop.Hangfire.HangfireWorkScheduler>();
        scope.ServiceProvider.GetRequiredService<IWorkInvoker>().Should().BeOfType<WorkInvoker>();
    }
}
