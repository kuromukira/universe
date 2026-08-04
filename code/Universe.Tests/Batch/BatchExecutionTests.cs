using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Universe.Attributes;
using Universe.Builder.Options;
using Universe.Exception;
using Universe.Extensions;
using Universe.Interfaces;
using Universe.Response;
using Universe.Tests.Helpers;
using Xunit;

namespace Universe.Tests.Batch;

public sealed class BatchExecutionTests
{
    [Fact]
    public async Task Atomic_MixedOperationsCommitInOrderAndUpdateCache()
    {
        InMemoryCosmosContainer<BatchEntity> container = new();
        BatchEntity replace = Entity("replace", "tenant-1", 1, "queued");
        BatchEntity patch = Entity("patch", "tenant-1", 2, "active");
        BatchEntity delete = Entity("delete", "tenant-1", 3, "obsolete");
        container.Seed(replace, "etag-replace");
        container.Seed(patch, "etag-patch");
        container.Seed(delete, "etag-delete");
        container.PatchConditionEvaluator = (model, predicate) =>
            model.Status == "active" && predicate == "FROM c WHERE c[\"status\"] = \"active\"";
        TestGalaxy repository = CreateRepository(container, cache: true);
        await repository.PointGet(patch.id, patch.TenantId);
        BatchEntity create = Entity("create", "tenant-1", 4, "new");
        replace.Quantity = 10;

        AtomicBatchResult<BatchEntity> result = await repository.Atomic("tenant-1")
            .Create(create)
            .Replace(replace, "etag-replace")
            .Patch(
                patch.id,
                operations => operations.Increment(entity => entity.Quantity, 5),
                "etag-patch",
                condition => condition.Equal(entity => entity.Status, "active"))
            .Delete(delete.id, "etag-delete")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(5, result.Gravity.RU);
        Assert.Equal(
            [BatchOperationKind.Create, BatchOperationKind.Replace, BatchOperationKind.Patch, BatchOperationKind.Delete],
            result.Operations.Select(operation => operation.Kind));
        Assert.All(result.Operations.Take(3), operation => Assert.False(string.IsNullOrWhiteSpace(operation.ETag)));
        Assert.Equal(4, result.Operations.Count);

        Assert.Equal(4, container.Find(create).Value.Model.Quantity);
        Assert.Equal(10, container.Find(replace).Value.Model.Quantity);
        Assert.Equal(7, container.Find(patch).Value.Model.Quantity);
        Assert.Null(container.Find(delete));

        InMemoryBatchExecution<BatchEntity> execution = Assert.Single(container.Executions);
        Assert.Equal(HttpStatusCode.OK, execution.StatusCode);
        Assert.Equal(5, execution.RequestCharge);
        Assert.Equal(result.Operations.Select(operation => operation.StatusCode), execution.Outcomes.Select(outcome => outcome.StatusCode));
        InMemoryBatchOperation<BatchEntity> patchOperation = execution.Operations[2];
        Assert.Equal("etag-patch", patchOperation.IfMatchETag);
        Assert.Equal("FROM c WHERE c[\"status\"] = \"active\"", patchOperation.FilterPredicate);
        Assert.IsType<TransactionalBatchPatchItemRequestOptions>(patchOperation.RequestOptions);

        (Gravity createCache, BatchEntity cachedCreate) = await repository.PointGet(create.id, create.TenantId);
        (Gravity replaceCache, BatchEntity cachedReplace) = await repository.PointGet(replace.id, replace.TenantId);
        (Gravity patchRead, BatchEntity patched) = await repository.PointGet(patch.id, patch.TenantId);
        Assert.Equal(0, createCache.RU);
        Assert.Equal(0, replaceCache.RU);
        Assert.Equal(1, patchRead.RU);
        Assert.Equal(4, cachedCreate.Quantity);
        Assert.Equal(10, cachedReplace.Quantity);
        Assert.Equal(7, patched.Quantity);
    }

    [Fact]
    public async Task Atomic_FailedPatchConditionRollsBackEarlierOperations()
    {
        InMemoryCosmosContainer<BatchEntity> container = new();
        BatchEntity replace = Entity("replace", "tenant-1", 1, "queued");
        BatchEntity patch = Entity("patch", "tenant-1", 2, "inactive");
        container.Seed(replace, "etag-replace");
        container.Seed(patch, "etag-patch");
        container.PatchConditionEvaluator = (model, predicate) =>
            model.Status == "active" && predicate == "FROM c WHERE c[\"status\"] = \"active\"";
        TestGalaxy repository = CreateRepository(container);
        replace.Quantity = 11;

        AtomicBatchResult<BatchEntity> result = await repository.Atomic("tenant-1")
            .Replace(replace, "etag-replace")
            .Patch(
                patch.id,
                operations => operations.Increment(entity => entity.Quantity, 5),
                "etag-patch",
                condition => condition.Equal(entity => entity.Status, "active"))
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(HttpStatusCode.PreconditionFailed, result.StatusCode);
        Assert.Equal(HttpStatusCode.FailedDependency, result.Operations[0].StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, result.Operations[1].StatusCode);
        Assert.Equal(1, container.Find(replace).Value.Model.Quantity);
        Assert.Equal(2, container.Find(patch).Value.Model.Quantity);
    }

    [Fact]
    public async Task Atomic_StaleETagRollsBackAndPreservesCache()
    {
        InMemoryCosmosContainer<BatchEntity> container = new();
        BatchEntity first = Entity("first", "tenant-1", 1, "active");
        BatchEntity second = Entity("second", "tenant-1", 2, "active");
        container.Seed(first, "etag-first");
        container.Seed(second, "etag-second");
        TestGalaxy repository = CreateRepository(container, cache: true);
        await repository.PointGet(first.id, first.TenantId);
        first.Quantity = 11;
        second.Quantity = 22;

        AtomicBatchResult<BatchEntity> result = await repository.Atomic("tenant-1")
            .Replace(first, "etag-first")
            .Replace(second, "stale-etag")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(HttpStatusCode.PreconditionFailed, result.StatusCode);
        Assert.Equal(HttpStatusCode.FailedDependency, result.Operations[0].StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, result.Operations[1].StatusCode);
        Assert.Equal(1, container.Find(first).Value.Model.Quantity);
        Assert.Equal(2, container.Find(second).Value.Model.Quantity);

        (Gravity cachedGravity, BatchEntity cached) = await repository.PointGet(first.id, first.TenantId);
        Assert.Equal(0, cachedGravity.RU);
        Assert.Equal(1, cached.Quantity);
    }

    [Fact]
    public async Task Bulk_ChunksAtOneHundredAndPreservesPartitionOrder()
    {
        InMemoryCosmosContainer<BatchEntity> container = new();
        TestGalaxy repository = CreateRepository(container);
        BulkBatch<BatchEntity> batch = repository.Bulk();
        for (int index = 0; index < 205; index++)
            batch.Create(Entity($"item-{index:D3}", "tenant-1", index, "new"));

        BulkExecutionResult<BatchEntity> result = await batch.ExecuteAsync(
            new() { MaxConcurrency = 4 },
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(205, result.SucceededCount);
        Assert.Equal([100, 100, 5], container.Executions.Select(execution => execution.Operations.Count));
        Assert.Equal(
            Enumerable.Range(0, 205).Select(index => $"item-{index:D3}"),
            container.Executions.SelectMany(execution => execution.Operations).Select(operation => operation.Id));
        Assert.Equal(1, container.MaxConcurrentExecutions);
    }

    [Fact]
    public async Task Bulk_RespectsConcurrencyAndReportsCrossPartitionPartialSuccess()
    {
        InMemoryCosmosContainer<BatchEntity> container = new() { ExecutionReleaseThreshold = 2 };
        BatchEntity duplicate = Entity("duplicate", "tenant-bad", 1, "existing");
        container.Seed(duplicate);
        TestGalaxy repository = CreateRepository(container, cache: true);
        await repository.PointGet(duplicate.id, duplicate.TenantId);
        BulkBatch<BatchEntity> batch = repository.Bulk()
            .Create(Entity("good-a", "tenant-a", 1, "new"))
            .Create(duplicate)
            .Create(Entity("good-c", "tenant-c", 1, "new"))
            .Create(Entity("good-d", "tenant-d", 1, "new"));

        BulkExecutionResult<BatchEntity> result = await batch.ExecuteAsync(
            new() { MaxConcurrency = 2 },
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(result.IsPartialSuccess);
        Assert.Equal(3, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal(2, container.MaxConcurrentExecutions);
        Assert.Equal(4, container.Executions.Count);

        (Gravity goodCache, BatchEntity good) = await repository.PointGet("good-a", "tenant-a");
        (Gravity duplicateCache, BatchEntity unchanged) = await repository.PointGet(duplicate.id, duplicate.TenantId);
        Assert.Equal(0, goodCache.RU);
        Assert.Equal(0, duplicateCache.RU);
        Assert.Equal("new", good.Status);
        Assert.Equal("existing", unchanged.Status);
    }

    [Fact]
    public async Task Batch_PreCancellationPerformsNoOperations()
    {
        InMemoryCosmosContainer<BatchEntity> container = new();
        TestGalaxy repository = CreateRepository(container);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.Bulk()
            .Create(Entity("cancelled", "tenant-1", 1, "new"))
            .ExecuteAsync(cancellationToken: cancellation.Token));

        Assert.Empty(container.Executions);
        Assert.Null(container.Find(Entity("cancelled", "tenant-1", 1, "new")));
    }

    [Fact]
    public async Task Batch_TransportFailureUsesUniverseExceptionContract()
    {
        InMemoryCosmosContainer<BatchEntity> container = new();
        container.FailNextExecution(new CosmosException(
            "transport failure",
            HttpStatusCode.ServiceUnavailable,
            0,
            "in-memory",
            0));
        TestGalaxy repository = CreateRepository(container);

        UniverseException exception = await Assert.ThrowsAsync<UniverseException>(() => repository.Atomic("tenant-1")
            .Create(Entity("transport", "tenant-1", 1, "new"))
            .ExecuteAsync(TestContext.Current.CancellationToken));

        Assert.IsType<CosmosException>(exception.InnerException);
        Assert.Empty(container.Executions);
    }

    private static TestGalaxy CreateRepository(InMemoryCosmosContainer<BatchEntity> container, bool cache = false)
    {
        UniverseOptions options = new UniverseOptions().WithAutoProvisioning(false);
        if (cache)
            options.WithDocumentCache();
        return new(container, options);
    }

    private static BatchEntity Entity(string id, string tenantId, int quantity, string status)
        => new() { id = id, TenantId = tenantId, Quantity = quantity, Status = status };

    private sealed class TestGalaxy : Galaxy<BatchEntity>
    {
        public TestGalaxy(InMemoryCosmosContainer<BatchEntity> container, UniverseOptions options)
            : base(CreateClient(), "database", "container", typeof(BatchEntity).BuildPartitionKey(), options)
        {
            FieldInfo containerField = typeof(GalaxyCore)
                .GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GalaxyCore._container was not found.");
            containerField.SetValue(this, container);
        }

        public AtomicBatch<BatchEntity> Atomic(params string[] partitionKeys)
            => ((IGalaxyBasic<BatchEntity>)this).Atomic(partitionKeys);

        public BulkBatch<BatchEntity> Bulk()
            => ((IGalaxyBasic<BatchEntity>)this).Bulk();

        public Task<(Gravity, BatchEntity)> PointGet(string id, string tenantId)
            => ((IGalaxyBasic<BatchEntity>)this).Get(id, tenantId);

        private static CosmosClient CreateClient()
            => new(
                "https://localhost:8081",
                Convert.ToBase64String(new byte[64]),
                new CosmosClientOptions
                {
                    AllowBulkExecution = true,
                    Serializer = new UniverseSerializer(JsonNamingPolicy.CamelCase)
                });
    }

    private sealed record BatchEntity : CosmicEntity
    {
        [PartitionKey]
        public string TenantId { get; set; }

        public int Quantity { get; set; }
        public string Status { get; set; }
    }
}
