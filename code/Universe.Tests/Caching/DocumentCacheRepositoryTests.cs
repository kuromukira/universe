using System.Collections;
using System.Net;
using System.Reflection;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Universe.Attributes;
using Universe.Extensions;
using Universe.Interfaces;
using Universe.Response;
using Xunit;

namespace Universe.Tests.Caching;

public sealed class DocumentCacheRepositoryTests
{
    [Fact]
    public async Task PointGet_CacheDisabled_ReadsContainerEveryTime()
    {
        FakeContainer container = new()
        {
            ReadItemResult = new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "from-cosmos" }
        };
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false));

        (_, CacheEntity first) = await repo.PointGet("id-1", "tenant-1");
        (_, CacheEntity second) = await repo.PointGet("id-1", "tenant-1");

        Assert.Equal("from-cosmos", first.Name);
        Assert.Equal("from-cosmos", second.Name);
        Assert.Equal(2, container.ReadItemCalls);
    }

    [Fact]
    public async Task PointGet_CacheEnabled_ReturnsSecondReadFromCache()
    {
        FakeContainer container = new()
        {
            ReadItemResult = new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "from-cosmos" }
        };
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false).WithDocumentCache());

        (Gravity firstGravity, CacheEntity first) = await repo.PointGet("id-1", "tenant-1");
        first.Name = "caller-mutation";
        (Gravity secondGravity, CacheEntity second) = await repo.PointGet("id-1", "tenant-1");

        Assert.Equal(9.5, firstGravity.RU);
        Assert.Equal(0, secondGravity.RU);
        Assert.Equal("from-cosmos", second.Name);
        Assert.Equal(1, container.ReadItemCalls);
    }

    [Fact]
    public async Task PointGet_CacheEnabled_PreservesETag()
    {
        FakeContainer container = new()
        {
            ReadItemResult = new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "from-cosmos" }
        };
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false).WithDocumentCache());

        (Gravity firstGravity, _) = await repo.PointGet("id-1", "tenant-1");
        (Gravity cachedGravity, _) = await repo.PointGet("id-1", "tenant-1");

        Assert.Equal("fake-etag", firstGravity.ETag);
        Assert.Equal("fake-etag", cachedGravity.ETag);
    }

    [Fact]
    public async Task AtomicCreate_ExecutesOneBatchAndUpdatesPointCacheAfterCommit()
    {
        FakeContainer container = new();
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false).WithDocumentCache());
        CacheEntity entity = new() { TenantId = "tenant-1", Name = "atomic" };

        AtomicBatchResult<CacheEntity> result = await repo.Atomic("tenant-1").Create(entity).ExecuteAsync(TestContext.Current.CancellationToken);
        (Gravity cachedGravity, CacheEntity cached) = await repo.PointGet(entity.id, "tenant-1");

        Assert.True(result.Succeeded);
        Assert.Single(result.Operations);
        Assert.Equal(3.5, result.Gravity.RU);
        Assert.Equal(1, container.TransactionalBatchCalls);
        Assert.Equal(0, cachedGravity.RU);
        Assert.Equal("atomic", cached.Name);
        Assert.Equal(0, container.ReadItemCalls);
    }

    [Fact]
    public async Task QueryGet_CacheEnabled_UsesStableKeyAcrossGeneratedCatalystIds()
    {
        FakeContainer container = new()
        {
            QueryItems = [new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "from-cosmos" }]
        };
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false).WithDocumentCache());

        (Gravity firstGravity, CacheEntity first) = await repo.QueryGet("same-name");
        (Gravity secondGravity, CacheEntity second) = await repo.QueryGet("same-name");

        Assert.Equal(7.25, firstGravity.RU);
        Assert.Equal(0, secondGravity.RU);
        Assert.Equal("from-cosmos", first.Name);
        Assert.Equal("from-cosmos", second.Name);
        Assert.Equal(1, container.QueryCalls);
    }

    [Fact]
    public async Task QueryGet_CacheHit_PreservesRecordedQueryDetails()
    {
        FakeContainer container = new()
        {
            QueryItems = [new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "from-cosmos" }]
        };
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false).WithDocumentCache(), recordQueries: true);

        await repo.QueryGet("same-name");
        (Gravity secondGravity, _) = await repo.QueryGet("same-name");

        Assert.Equal(0, secondGravity.RU);
        Assert.StartsWith("SELECT", secondGravity.Query.Text);
        Assert.NotEmpty(secondGravity.Query.Parameters);
        Assert.Equal(1, container.QueryCalls);
    }

    [Fact]
    public async Task Modify_UpdatesPointReadCache()
    {
        FakeContainer container = new()
        {
            ReadItemResult = new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "before" },
            ReplaceItemResult = new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "after" }
        };
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false).WithDocumentCache());

        await repo.PointGet("id-1", "tenant-1");
        await repo.ModifyEntity(new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "after" });
        (Gravity gravity, CacheEntity cached) = await repo.PointGet("id-1", "tenant-1");

        Assert.Equal(0, gravity.RU);
        Assert.Equal("after", cached.Name);
        Assert.Equal(1, container.ReadItemCalls);
    }

    [Fact]
    public async Task Modify_ClearsSingleQueryCacheForRepositoryScope()
    {
        FakeContainer container = new()
        {
            QueryItems = [new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "before" }],
            ReplaceItemResult = new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "after" }
        };
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false).WithDocumentCache());

        await repo.QueryGet("same-name");
        await repo.QueryGet("same-name");
        await repo.ModifyEntity(new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "after" });
        container.QueryItems = [new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "after" }];
        (_, CacheEntity afterInvalidation) = await repo.QueryGet("same-name");

        Assert.Equal("after", afterInvalidation.Name);
        Assert.Equal(2, container.QueryCalls);
    }

    [Fact]
    public async Task Remove_InvalidatesPointReadCache()
    {
        FakeContainer container = new()
        {
            ReadItemResult = new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "before" }
        };
        TestGalaxy repo = new(container, new UniverseOptions().WithAutoProvisioning(false).WithDocumentCache());

        await repo.PointGet("id-1", "tenant-1");
        await repo.RemoveEntity("id-1", "tenant-1");
        container.ReadItemResult = new CacheEntity { id = "id-1", TenantId = "tenant-1", Name = "after" };
        (_, CacheEntity afterInvalidation) = await repo.PointGet("id-1", "tenant-1");

        Assert.Equal("after", afterInvalidation.Name);
        Assert.Equal(2, container.ReadItemCalls);
    }

    private sealed class TestGalaxy : Galaxy<CacheEntity>
    {
        public TestGalaxy(FakeContainer container, UniverseOptions options, bool recordQueries = false)
            : base(CreateClient(), "database", "container", typeof(CacheEntity).BuildPartitionKey(), options, recordQueries)
        {
            typeof(GalaxyCore)
                .GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(this, container);
        }

        public Task<(Gravity, CacheEntity)> PointGet(string id, string tenantId)
            => ((IGalaxyBasic<CacheEntity>)this).Get(id, tenantId);

        public Task<(Gravity, CacheEntity)> QueryGet(string name)
            => ((IGalaxy<CacheEntity>)this).Get([new([new("Name", name)])]);

        public Task<(Gravity, CacheEntity)> ModifyEntity(CacheEntity entity)
            => ((IGalaxyBasic<CacheEntity>)this).Modify(entity);

        public Task<Gravity> RemoveEntity(string id, string tenantId)
            => ((IGalaxyBasic<CacheEntity>)this).Remove(id, tenantId);

        public AtomicBatch<CacheEntity> Atomic(string tenantId)
            => ((IGalaxyBasic<CacheEntity>)this).Atomic(tenantId);

        private static CosmosClient CreateClient()
            => new(
                "https://localhost:8081",
                Convert.ToBase64String(new byte[64]),
                new CosmosClientOptions { AllowBulkExecution = true });
    }

    private sealed record CacheEntity : CosmicEntity
    {
        [PartitionKey]
        public string TenantId { get; set; }

        public string Name { get; set; }
    }

    private sealed class FakeContainer : Container
    {
        public CacheEntity ReadItemResult { get; set; }
        public CacheEntity ReplaceItemResult { get; set; }
        public IReadOnlyList<CacheEntity> QueryItems { get; set; } = [];
        public int ReadItemCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public int TransactionalBatchCalls { get; private set; }

        public override string Id => "container";
        public override Database Database => throw new NotSupportedException();
        public override Conflicts Conflicts => throw new NotSupportedException();
        public override Scripts Scripts => throw new NotSupportedException();

        public override Task<ItemResponse<T>> ReadItemAsync<T>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        {
            ReadItemCalls++;
            return Task.FromResult<ItemResponse<T>>(new FakeItemResponse<T>((T)(object)ReadItemResult, 9.5));
        }

        public override Task<ItemResponse<T>> ReplaceItemAsync<T>(T item, string id, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ItemResponse<T>>(new FakeItemResponse<T>((T)(object)ReplaceItemResult, 6.5));

        public override Task<ItemResponse<T>> DeleteItemAsync<T>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ItemResponse<T>>(new FakeItemResponse<T>(default, 4.5));

        public override FeedIterator<T> GetItemQueryIterator<T>(QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null)
        {
            QueryCalls++;
            return new FakeFeedIterator<T>(QueryItems.Cast<T>().ToArray(), 7.25);
        }

        public override Task<ItemResponse<T>> CreateItemAsync<T>(T item, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ItemResponse<T>>(new FakeItemResponse<T>(item, 5.5));

        public override TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey)
        {
            TransactionalBatchCalls++;
            return new FakeTransactionalBatch();
        }

        public override Task<ContainerResponse> ReadContainerAsync(ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> ReadContainerStreamAsync(ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ContainerResponse> ReplaceContainerAsync(ContainerProperties containerProperties, ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> ReplaceContainerStreamAsync(ContainerProperties containerProperties, ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ContainerResponse> DeleteContainerAsync(ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> DeleteContainerStreamAsync(ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<int?> ReadThroughputAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ThroughputResponse> ReadThroughputAsync(RequestOptions requestOptions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ThroughputResponse> ReplaceThroughputAsync(int throughput, RequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ThroughputResponse> ReplaceThroughputAsync(ThroughputProperties throughputProperties, RequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> CreateItemStreamAsync(Stream streamPayload, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> ReadItemStreamAsync(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> UpsertItemStreamAsync(Stream streamPayload, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ItemResponse<T>> UpsertItemAsync<T>(T item, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> ReplaceItemStreamAsync(Stream streamPayload, string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<FeedResponse<T>> ReadManyItemsAsync<T>(IReadOnlyList<(string id, PartitionKey partitionKey)> items, ReadManyRequestOptions readManyRequestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> ReadManyItemsStreamAsync(IReadOnlyList<(string id, PartitionKey partitionKey)> items, ReadManyRequestOptions readManyRequestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ItemResponse<T>> PatchItemAsync<T>(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> PatchItemStreamAsync(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<ResponseMessage> DeleteItemStreamAsync(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override FeedIterator GetItemQueryStreamIterator(QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
        public override FeedIterator GetItemQueryStreamIterator(string queryText = null, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
        public override FeedIterator<T> GetItemQueryIterator<T>(string queryText = null, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
        public override FeedIterator GetItemQueryStreamIterator(FeedRange feedRange, QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
        public override FeedIterator<T> GetItemQueryIterator<T>(FeedRange feedRange, QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
        public override IOrderedQueryable<T> GetItemLinqQueryable<T>(bool allowSynchronousQueryExecution = false, string continuationToken = null, QueryRequestOptions requestOptions = null, CosmosLinqSerializerOptions linqSerializerOptions = null) => throw new NotSupportedException();
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(string processorName, ChangesHandler<T> onChangesDelegate) => throw new NotSupportedException();
        public override ChangeFeedProcessorBuilder GetChangeFeedEstimatorBuilder(string processorName, ChangesEstimationHandler estimationDelegate, TimeSpan? estimationPeriod = null) => throw new NotSupportedException();
        public override ChangeFeedEstimator GetChangeFeedEstimator(string processorName, Container leaseContainer) => throw new NotSupportedException();
        public override Task<IReadOnlyList<FeedRange>> GetFeedRangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override FeedIterator GetChangeFeedStreamIterator(ChangeFeedStartFrom changeFeedStartFrom, ChangeFeedMode changeFeedMode, ChangeFeedRequestOptions changeFeedRequestOptions = null) => throw new NotSupportedException();
        public override FeedIterator<T> GetChangeFeedIterator<T>(ChangeFeedStartFrom changeFeedStartFrom, ChangeFeedMode changeFeedMode, ChangeFeedRequestOptions changeFeedRequestOptions = null) => throw new NotSupportedException();
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(string processorName, ChangeFeedHandler<T> onChangesDelegate) => throw new NotSupportedException();
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint<T>(string processorName, ChangeFeedHandlerWithManualCheckpoint<T> onChangesDelegate) => throw new NotSupportedException();
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder(string processorName, ChangeFeedStreamHandler onChangesDelegate) => throw new NotSupportedException();
        public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint(string processorName, ChangeFeedStreamHandlerWithManualCheckpoint onChangesDelegate) => throw new NotSupportedException();
    }

    private sealed class FakeItemResponse<T>(T resource, double requestCharge) : ItemResponse<T>
    {
        public override T Resource => resource;
        public override double RequestCharge => requestCharge;
        public override string ETag => "fake-etag";
        public override HttpStatusCode StatusCode => HttpStatusCode.OK;
    }

    private sealed class FakeFeedIterator<T>(IReadOnlyList<T> items, double requestCharge) : FeedIterator<T>
    {
        private bool _hasMoreResults = true;

        public override bool HasMoreResults => _hasMoreResults;

        public override Task<FeedResponse<T>> ReadNextAsync(CancellationToken cancellationToken = default)
        {
            _hasMoreResults = false;
            return Task.FromResult<FeedResponse<T>>(new FakeFeedResponse<T>(items, requestCharge));
        }
    }

    private sealed class FakeFeedResponse<T>(IReadOnlyList<T> items, double requestCharge) : FeedResponse<T>
    {
        public override string ContinuationToken => null;
        public override int Count => items.Count;
        public override string IndexMetrics => string.Empty;
        public override Headers Headers => null;
        public override IEnumerable<T> Resource => items;
        public override HttpStatusCode StatusCode => HttpStatusCode.OK;
        public override CosmosDiagnostics Diagnostics => null;
        public override double RequestCharge => requestCharge;
        public override IEnumerator<T> GetEnumerator() => items.GetEnumerator();
    }

    private sealed class FakeTransactionalBatch : TransactionalBatch
    {
        public override TransactionalBatch CreateItem<T>(T item, TransactionalBatchItemRequestOptions requestOptions = null) => this;
        public override TransactionalBatch CreateItemStream(Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null) => this;
        public override TransactionalBatch ReadItem(string id, TransactionalBatchItemRequestOptions requestOptions = null) => this;
        public override TransactionalBatch UpsertItem<T>(T item, TransactionalBatchItemRequestOptions requestOptions = null) => this;
        public override TransactionalBatch UpsertItemStream(Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null) => this;
        public override TransactionalBatch ReplaceItem<T>(string id, T item, TransactionalBatchItemRequestOptions requestOptions = null) => this;
        public override TransactionalBatch ReplaceItemStream(string id, Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null) => this;
        public override TransactionalBatch DeleteItem(string id, TransactionalBatchItemRequestOptions requestOptions = null) => this;
        public override TransactionalBatch PatchItem(string id, IReadOnlyList<PatchOperation> patchOperations, TransactionalBatchPatchItemRequestOptions requestOptions = null) => this;
        public override Task<TransactionalBatchResponse> ExecuteAsync(CancellationToken cancellationToken = default) => Task.FromResult<TransactionalBatchResponse>(new FakeTransactionalBatchResponse());
        public override Task<TransactionalBatchResponse> ExecuteAsync(TransactionalBatchRequestOptions requestOptions, CancellationToken cancellationToken = default) => Task.FromResult<TransactionalBatchResponse>(new FakeTransactionalBatchResponse());
    }

    private sealed class FakeTransactionalBatchResponse : TransactionalBatchResponse
    {
        public override bool IsSuccessStatusCode => true;
        public override HttpStatusCode StatusCode => HttpStatusCode.OK;
        public override double RequestCharge => 3.5;
    }
}
