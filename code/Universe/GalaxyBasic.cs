using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Universe.Builder;
using Universe.Builder.Caching;
using Universe.Builder.Strategies;
using Universe.Response;

namespace Universe;

/// <summary>Inherit repositories to implement the very basic Universe</summary>
public class GalaxyBasic<T> : GalaxyCore, IGalaxyBasic<T> where T : class, ICosmicEntity
{
    internal readonly UniverseBuilder QBuilder;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            QBuilder?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Create a new Galaxy with default settings</summary>
    protected GalaxyBasic(
        CosmosClient client,
        string database,
        string container,
        IReadOnlyList<string> partitionKey,
        bool recordQueries = false) : base(client, database, container, partitionKey, recordQueries) => QBuilder = new(_recordQuery, _namingPolicy);

    /// <summary>Create a new Galaxy with custom Universe options</summary>
    protected GalaxyBasic(
        CosmosClient client,
        string database,
        string container,
        IReadOnlyList<string> partitionKey,
        UniverseOptions options,
        bool recordQueries = false) : base(client, database, container, partitionKey, options, recordQueries)
    {
        QueryTuner queryTuner = new(options.StatisticsStorage);
        QBuilder = new(_recordQuery, queryTuner, _namingPolicy);
    }

    async Task<(Gravity, string)> IGalaxyBasic<T>.Create(T model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.id))
                model.id = Guid.CreateVersion7().ToString();
            model.AddedOn = DateTime.UtcNow;

            ItemResponse<T> response = await _container.CreateItemAsync(
                model,
                model.BuildPartitionKey(),
                requestOptions: new()
                {
                    EnableContentResponseOnWrite = false
                });
            SetPointCache(model, response.ETag);
            ClearQueryCache();
            return (new(response.RequestCharge, null) { ETag = response.ETag }, model.id);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new UniverseException($"{typeof(T).Name} already exists.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.Conflict)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    async Task<Gravity> IGalaxyBasic<T>.Create(IReadOnlyList<T> models)
    {
        try
        {
            if (!_allowBulk)
                throw new UniverseException("Bulk create of documents is not configured properly.");

            string payload = JsonSerializer.Serialize(models);
            if (Encoding.UTF8.GetByteCount(payload) > 2 * 1024 * 1024)
                throw new UniverseException("Payload size exceeds the maximum allowed size of 2MB.");

            if (models.Count > 100)
                throw new UniverseException("Bulk create can only handle up to 100 items at a time.");

            List<BatchOperation<T>> operations = models
                .Select((model, index) => BatchOperation<T>.Create(index, model, [.. model.PartitionKeys()]))
                .ToList();
            foreach (BatchOperation<T> operation in operations)
                operation.PrepareForExecution();

            BatchChunkResult<T>[] batches = await Task.WhenAll(operations
                .GroupBy(operation => PartitionKeyGroup(operation.PartitionKeys))
                .Select(group => TransactionalBatchExecutor<T>.ExecuteAsync(_container, group.ToArray(), CancellationToken.None)));

            BatchChunkResult<T> failed = batches.FirstOrDefault(batch => !batch.Succeeded);
            if (failed is not null)
            {
                if (failed.StatusCode == HttpStatusCode.Conflict)
                    throw new UniverseException($"{typeof(T).Name} already exists.");

                throw new UniverseException($"Transaction batch failed with status code {failed.StatusCode}.");
            }

            double totalRu = batches.Sum(batch => batch.RU);

            foreach (T model in models)
                SetPointCache(model);

            ClearQueryCache();
            return new(totalRu, string.Empty);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new UniverseException($"{typeof(T).Name} already exists.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.Conflict)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    async Task<(Gravity, T)> IGalaxyBasic<T>.Modify(T model)
    {
        try
        {
            model.ModifiedOn = DateTime.UtcNow;

            ItemResponse<T> response = await _container.ReplaceItemAsync(model, model.id, model.BuildPartitionKey());
            SetPointCache(response.Resource ?? model, response.ETag);
            ClearQueryCache();
            return (new(response.RequestCharge, null) { ETag = response.ETag }, response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    async Task<Gravity> IGalaxyBasic<T>.Modify(IReadOnlyList<T> models)
    {
        try
        {
            if (!_allowBulk)
                throw new UniverseException("Bulk modify of documents is not configured properly.");

            string payload = JsonSerializer.Serialize(models);
            if (Encoding.UTF8.GetByteCount(payload) > 2 * 1024 * 1024)
                throw new UniverseException("Payload size exceeds the maximum allowed size of 2MB.");

            if (models.Count > 100)
                throw new UniverseException("Bulk modify can only handle up to 100 items at a time.");

            List<BatchOperation<T>> operations = models
                .Select((model, index) => BatchOperation<T>.Replace(index, model, [.. model.PartitionKeys()], null))
                .ToList();
            foreach (BatchOperation<T> operation in operations)
                operation.PrepareForExecution();

            BatchChunkResult<T>[] batches = await Task.WhenAll(operations
                .GroupBy(operation => PartitionKeyGroup(operation.PartitionKeys))
                .Select(group => TransactionalBatchExecutor<T>.ExecuteAsync(_container, group.ToArray(), CancellationToken.None)));

            BatchChunkResult<T> failed = batches.FirstOrDefault(batch => !batch.Succeeded);
            if (failed is not null)
                throw new UniverseException($"Transaction batch failed with status code {failed.StatusCode}.");

            double totalRu = batches.Sum(batch => batch.RU);

            foreach (T model in models)
                SetPointCache(model);

            ClearQueryCache();
            return new(totalRu, string.Empty);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException("Bulk modify operation failed.", ex);
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    internal static PartitionKey BuildPartitionKey(IReadOnlyList<string> partitionKey)
    {
        if (partitionKey is null || partitionKey.Count == 0)
            throw new UniverseException("Partition key cannot be null or empty.");

        PartitionKeyBuilder partitionKeyBuilder = new();
        foreach (string key in partitionKey)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new UniverseException("Partition key value cannot be null or empty.");

            partitionKeyBuilder.Add(key);
        }

        return partitionKeyBuilder.Build();
    }

    async Task<Gravity> IGalaxyBasic<T>.Remove(string id, params string[] partitionKey)
    {
        try
        {
            ItemResponse<T> response = await _container.DeleteItemAsync<T>(id, BuildPartitionKey(partitionKey), requestOptions: new()
            {
                EnableContentResponseOnWrite = false
            });
            RemovePointCache(id, partitionKey);
            ClearQueryCache();
            return new(response.RequestCharge, null);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    async Task<Gravity> IGalaxyBasic<T>.Remove(string id, string partitionKey)
    {
        try
        {
            ItemResponse<T> response = await _container.DeleteItemAsync<T>(id, new(partitionKey), requestOptions: new()
            {
                EnableContentResponseOnWrite = false
            });
            RemovePointCache(id, [partitionKey]);
            ClearQueryCache();
            return new(response.RequestCharge, null);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    async Task<(Gravity g, T T)> IGalaxyBasic<T>.Get(string id, params string[] partitionKey)
    {
        try
        {
            if (TryGetPointCache(id, partitionKey, out T cached, out string cachedETag))
                return (new(0, null) { ETag = cachedETag }, cached);

            ItemResponse<T> response = await _container.ReadItemAsync<T>(id, BuildPartitionKey(partitionKey));
            SetPointCache(id, partitionKey, response.Resource, response.ETag);
            return (new(response.RequestCharge, null) { ETag = response.ETag }, response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    async Task<(Gravity, T)> IGalaxyBasic<T>.Get(string id, string partitionKey)
    {
        try
        {
            if (TryGetPointCache(id, [partitionKey], out T cached, out string cachedETag))
                return (new(0, null) { ETag = cachedETag }, cached);

            ItemResponse<T> response = await _container.ReadItemAsync<T>(id, new(partitionKey));
            SetPointCache(id, [partitionKey], response.Resource, response.ETag);
            return (new(response.RequestCharge, null) { ETag = response.ETag }, response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    /// <inheritdoc/>
    AtomicBatch<T> IGalaxyBasic<T>.Atomic(params string[] partitionKeys)
    {
        _ = BuildPartitionKey(partitionKeys);
        return new(_namingPolicy, ExecuteAtomicAsync, [.. partitionKeys]);
    }

    /// <inheritdoc/>
    BulkBatch<T> IGalaxyBasic<T>.Bulk() => new(_namingPolicy, ExecuteBulkAsync);

    internal async Task<AtomicBatchResult<T>> ExecuteAtomicAsync(IReadOnlyList<BatchOperation<T>> operations, CancellationToken cancellationToken)
    {
        ValidateAtomicOperations(operations);
        foreach (BatchOperation<T> operation in operations)
            operation.PrepareForExecution();
        ValidatePayload(operations);

        BatchChunkResult<T> result = await TransactionalBatchExecutor<T>.ExecuteAsync(_container, operations, cancellationToken);
        if (result.Succeeded)
            ApplySuccessfulCommit(operations, result.Operations, clearQueryCache: true);

        return new(new(result.RU, null), result.Succeeded, result.StatusCode, result.Operations);
    }

    internal async Task<BulkExecutionResult<T>> ExecuteBulkAsync(
        IReadOnlyList<BatchOperation<T>> operations,
        BulkExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (operations is null || operations.Count == 0)
            throw new UniverseException("A batch must contain at least one operation.");
        if (options.MaxConcurrency < 1)
            throw new UniverseException("Bulk batch concurrency must be at least one.");

        foreach (BatchOperation<T> operation in operations)
        {
            operation.PrepareForExecution();
            if (operation.EstimatePayloadSize() > 2 * 1024 * 1024)
                throw new UniverseException("A batch operation payload exceeds the maximum allowed size of 2MB.");
        }

        ConcurrentBag<BatchChunkResult<T>> completed = [];
        int anySuccessfulCommit = 0;
        using SemaphoreSlim concurrency = new(options.MaxConcurrency, options.MaxConcurrency);
        try
        {
            await Task.WhenAll(operations
                .GroupBy(operation => PartitionKeyGroup(operation.PartitionKeys))
                .Select(async group =>
                {
                    await concurrency.WaitAsync(cancellationToken);
                    try
                    {
                        foreach (IReadOnlyList<BatchOperation<T>> chunk in Chunk(group.OrderBy(operation => operation.Index)))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            BatchChunkResult<T> result = await TransactionalBatchExecutor<T>.ExecuteAsync(_container, chunk, cancellationToken);
                            completed.Add(result);
                            if (result.Succeeded)
                            {
                                ApplySuccessfulCommit(chunk, result.Operations, clearQueryCache: false);
                                Interlocked.Exchange(ref anySuccessfulCommit, 1);
                            }
                        }
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }));
        }
        finally
        {
            if (Volatile.Read(ref anySuccessfulCommit) == 1)
                ClearQueryCache();
        }

        IReadOnlyList<BatchOperationResult<T>> orderedResults = completed
            .SelectMany(result => result.Operations)
            .OrderBy(result => result.Index)
            .ToArray();
        bool succeeded = completed.All(result => result.Succeeded) && orderedResults.Count == operations.Count;
        int succeededCount = orderedResults.Count(result => result.Succeeded);
        int failedCount = orderedResults.Count - succeededCount;
        double totalRu = completed.Sum(result => result.RU);
        HttpStatusCode statusCode = succeeded
            ? HttpStatusCode.OK
            : orderedResults.FirstOrDefault(result => !result.Succeeded)?.StatusCode ?? HttpStatusCode.InternalServerError;
        return new(new(totalRu, null), succeeded, statusCode, succeededCount > 0 && failedCount > 0, succeededCount, failedCount, orderedResults);
    }

    private static void ValidateAtomicOperations(IReadOnlyList<BatchOperation<T>> operations)
    {
        if (operations is null || operations.Count == 0)
            throw new UniverseException("A batch must contain at least one operation.");
        if (operations.Count > 100)
            throw new UniverseException("Atomic batches can only contain up to 100 operations.");

        string[] partitionKeys = operations[0].PartitionKeys;
        if (operations.Any(operation => !operation.PartitionKeys.SequenceEqual(partitionKeys, StringComparer.Ordinal)))
            throw new UniverseException("All atomic batch operations must use the same logical partition.");
    }

    private static void ValidatePayload(IReadOnlyList<BatchOperation<T>> operations)
    {
        int total = 0;
        foreach (BatchOperation<T> operation in operations)
        {
            int operationSize = operation.EstimatePayloadSize();
            if (operationSize > 2 * 1024 * 1024)
                throw new UniverseException("A batch operation payload exceeds the maximum allowed size of 2MB.");

            total = checked(total + operationSize);
        }

        if (total > 2 * 1024 * 1024)
            throw new UniverseException("Payload size exceeds the maximum allowed size of 2MB.");
    }

    private static IEnumerable<IReadOnlyList<BatchOperation<T>>> Chunk(IEnumerable<BatchOperation<T>> operations)
    {
        List<BatchOperation<T>> current = [];
        int currentSize = 0;
        foreach (BatchOperation<T> operation in operations)
        {
            int operationSize = operation.EstimatePayloadSize();
            if (current.Count == 100 || currentSize + operationSize > 2 * 1024 * 1024)
            {
                yield return current;
                current = [];
                currentSize = 0;
            }

            current.Add(operation);
            currentSize += operationSize;
        }

        if (current.Count > 0)
            yield return current;
    }

    private void ApplySuccessfulCommit(
        IReadOnlyList<BatchOperation<T>> operations,
        IReadOnlyList<BatchOperationResult<T>> results,
        bool clearQueryCache)
    {
        Dictionary<int, BatchOperationResult<T>> resultsByIndex = results.ToDictionary(result => result.Index);
        foreach (BatchOperation<T> operation in operations)
        {
            BatchOperationResult<T> result = resultsByIndex[operation.Index];
            if (operation.Kind is BatchOperationKind.Create or BatchOperationKind.Replace)
                SetPointCache(operation.Model, result.ETag);
            else
                RemovePointCache(operation.Id, operation.PartitionKeys);
        }

        if (clearQueryCache)
            ClearQueryCache();
    }

    private static string PartitionKeyGroup(IReadOnlyList<string> partitionKeys) => JsonSerializer.Serialize(partitionKeys);

    private bool TryGetPointCache(string id, IReadOnlyList<string> partitionKeys, out T value, out string eTag)
    {
        value = default;
        eTag = null;
        DocumentCache cache = DocumentCache;
        if (cache is null)
            return false;

        DocumentCacheKey key = cache.CreatePointKey(_databaseName, _containerName, typeof(T), typeof(T), id, partitionKeys);
        return cache.TryGet(key, out value, out eTag);
    }

    private void SetPointCache(T model, string eTag = null)
    {
        DocumentCache cache = DocumentCache;
        if (cache is null || model is null)
            return;

        SetPointCache(model.id, [.. model.PartitionKeys()], model, eTag);
    }

    private void SetPointCache(string id, IReadOnlyList<string> partitionKeys, T model, string eTag = null)
    {
        DocumentCache cache = DocumentCache;
        if (cache is null || model is null)
            return;

        DocumentCacheKey key = cache.CreatePointKey(_databaseName, _containerName, typeof(T), typeof(T), id, partitionKeys);
        cache.Set(key, DocumentCacheOperation.PointRead, DocumentCacheScopeHash(typeof(T)), model, eTag);
    }

    private void RemovePointCache(string id, IReadOnlyList<string> partitionKeys)
    {
        DocumentCache cache = DocumentCache;
        if (cache is null)
            return;

        DocumentCacheKey key = cache.CreatePointKey(_databaseName, _containerName, typeof(T), typeof(T), id, partitionKeys);
        cache.Remove(key);
    }

    private void ClearQueryCache()
    {
        DocumentCache cache = DocumentCache;
        if (cache is null)
            return;

        cache.ClearQueries(DocumentCacheScopeHash(typeof(T)));
    }
}
