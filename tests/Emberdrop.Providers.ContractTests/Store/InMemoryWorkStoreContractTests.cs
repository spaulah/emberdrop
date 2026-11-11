using Emberdrop.InMemory;
using Emberdrop.Storage;

namespace Emberdrop.Providers.ContractTests.Store;

public sealed class InMemoryWorkStoreContractTests : WorkStoreContract
{
    protected override Task<IWorkStore> CreateStoreAsync() => Task.FromResult<IWorkStore>(new InMemoryWorkStore());
}
