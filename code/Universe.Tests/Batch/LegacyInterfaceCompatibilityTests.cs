using Universe.Interfaces;
using Universe.Response;
using Xunit;

namespace Universe.Tests.Batch;

public sealed class LegacyInterfaceCompatibilityTests
{
    [Fact]
    public async Task LegacyImplementation_ExistingOperationsRemainCallable()
    {
        IGalaxyBasic<LegacyEntity> legacy = new LegacyGalaxy();
        LegacyEntity entity = new() { id = "legacy-id" };

        (Gravity gravity, string id) = await legacy.Create(entity);

        Assert.Equal("legacy-id", id);
        Assert.Equal(1, gravity.RU);
    }

    [Fact]
    public void LegacyImplementation_NewBatchOperationsFailClearly()
    {
        IGalaxyBasic<LegacyEntity> legacy = new LegacyGalaxy();

        NotSupportedException atomic = Assert.Throws<NotSupportedException>(() => legacy.Atomic("tenant-1"));
        NotSupportedException bulk = Assert.Throws<NotSupportedException>(() => legacy.Bulk());

        Assert.Contains("atomic batch operations", atomic.Message);
        Assert.Contains("bulk batch operations", bulk.Message);
    }

    private sealed class LegacyGalaxy : IGalaxyBasic<LegacyEntity>
    {
        public Task<(Gravity g, string t)> Create(LegacyEntity model)
            => Task.FromResult<(Gravity, string)>((new(1, null), model.id));

        public Task<Gravity> Create(IReadOnlyList<LegacyEntity> models)
            => Task.FromResult<Gravity>(new(models.Count, null));

        public Task<(Gravity g, LegacyEntity T)> Modify(LegacyEntity model)
            => Task.FromResult<(Gravity, LegacyEntity)>((new(1, null), model));

        public Task<Gravity> Modify(IReadOnlyList<LegacyEntity> models)
            => Task.FromResult<Gravity>(new(models.Count, null));

        public Task<Gravity> Remove(string id, string partitionKey)
            => Task.FromResult<Gravity>(new(1, null));

        public Task<Gravity> Remove(string id, params string[] partitionKey)
            => Task.FromResult<Gravity>(new(1, null));

        public Task<(Gravity g, LegacyEntity T)> Get(string id, string partitionKey)
            => Task.FromResult<(Gravity, LegacyEntity)>((new(1, null), new() { id = id }));

        public Task<(Gravity g, LegacyEntity T)> Get(string id, params string[] partitionKey)
            => Task.FromResult<(Gravity, LegacyEntity)>((new(1, null), new() { id = id }));
    }

    private sealed record LegacyEntity : CosmicEntity;
}
