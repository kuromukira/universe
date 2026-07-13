using System.Net;
using System.Text.Json;

namespace Universe;

/// <summary>Builds a same-partition ACID transaction.</summary>
public sealed class AtomicBatch<T> where T : ICosmicEntity
{
    private readonly Func<IReadOnlyList<BatchOperation<T>>, CancellationToken, Task<AtomicBatchResult<T>>> _execute;
    private readonly JsonNamingPolicy _namingPolicy;
    private readonly string[] _partitionKeys;
    private readonly List<BatchOperation<T>> _operations = [];

    internal AtomicBatch(
        JsonNamingPolicy namingPolicy,
        Func<IReadOnlyList<BatchOperation<T>>, CancellationToken, Task<AtomicBatchResult<T>>> execute,
        string[] partitionKeys)
    {
        _namingPolicy = namingPolicy;
        _execute = execute;
        _partitionKeys = partitionKeys;
    }

    /// <summary>Adds a create operation.</summary>
    public AtomicBatch<T> Create(T model)
    {
        ValidateModelPartition(model);
        _operations.Add(BatchOperation<T>.Create(_operations.Count, model, _partitionKeys));
        return this;
    }

    /// <summary>Adds an ETag-aware replacement operation.</summary>
    public AtomicBatch<T> Replace(T model, string eTag = null)
    {
        ValidateModelPartition(model);
        _operations.Add(BatchOperation<T>.Replace(_operations.Count, model, _partitionKeys, eTag));
        return this;
    }

    /// <summary>Adds an ETag-aware conditional patch operation.</summary>
    public AtomicBatch<T> Patch(
        string id,
        Action<PatchBuilder<T>> patch,
        string eTag = null,
        Action<PatchConditionBuilder<T>> condition = null)
    {
        _operations.Add(CreatePatchOperation(_operations.Count, id, _partitionKeys, patch, eTag, condition));
        return this;
    }

    /// <summary>Adds an ETag-aware deletion operation.</summary>
    public AtomicBatch<T> Delete(string id, string eTag = null)
    {
        _operations.Add(BatchOperation<T>.Delete(_operations.Count, id, _partitionKeys, eTag));
        return this;
    }

    /// <summary>Executes all operations as one same-partition Cosmos transaction.</summary>
    public Task<AtomicBatchResult<T>> ExecuteAsync(CancellationToken cancellationToken = default)
        => _execute(_operations, cancellationToken);

    private void ValidateModelPartition(T model)
    {
        if (model is null)
            throw new UniverseException("A batch model is required.");

        if (!model.PartitionKeys().SequenceEqual(_partitionKeys, StringComparer.Ordinal))
            throw new UniverseException("All atomic batch operations must use the partition keys provided to Atomic().");
    }

    private BatchOperation<T> CreatePatchOperation(int index, string id, string[] partitionKeys, Action<PatchBuilder<T>> configurePatch, string eTag, Action<PatchConditionBuilder<T>> configureCondition)
        => BatchOperationFactory.CreatePatch(index, id, partitionKeys, _namingPolicy, configurePatch, eTag, configureCondition);
}

/// <summary>Builds independently transactional operations across logical partitions.</summary>
public sealed class BulkBatch<T> where T : ICosmicEntity
{
    private readonly Func<IReadOnlyList<BatchOperation<T>>, BulkExecutionOptions, CancellationToken, Task<BulkExecutionResult<T>>> _execute;
    private readonly JsonNamingPolicy _namingPolicy;
    private readonly List<BatchOperation<T>> _operations = [];

    internal BulkBatch(
        JsonNamingPolicy namingPolicy,
        Func<IReadOnlyList<BatchOperation<T>>, BulkExecutionOptions, CancellationToken, Task<BulkExecutionResult<T>>> execute)
    {
        _namingPolicy = namingPolicy;
        _execute = execute;
    }

    /// <summary>Adds a create operation. The partition key is derived from the model.</summary>
    public BulkBatch<T> Create(T model)
    {
        if (model is null)
            throw new UniverseException("A batch model is required.");

        _operations.Add(BatchOperation<T>.Create(_operations.Count, model, [.. model.PartitionKeys()]));
        return this;
    }

    /// <summary>Adds a replacement operation. The partition key is derived from the model.</summary>
    public BulkBatch<T> Replace(T model, string eTag = null)
    {
        if (model is null)
            throw new UniverseException("A batch model is required.");

        _operations.Add(BatchOperation<T>.Replace(_operations.Count, model, [.. model.PartitionKeys()], eTag));
        return this;
    }

    /// <summary>Adds a patch operation. Partition keys are required because no model is supplied.</summary>
    public BulkBatch<T> Patch(
        string id,
        string[] partitionKeys,
        Action<PatchBuilder<T>> patch,
        string eTag = null,
        Action<PatchConditionBuilder<T>> condition = null)
    {
        _operations.Add(BatchOperationFactory.CreatePatch(_operations.Count, id, partitionKeys, _namingPolicy, patch, eTag, condition));
        return this;
    }

    /// <summary>Adds a delete operation. Partition keys are required because no model is supplied.</summary>
    public BulkBatch<T> Delete(string id, string[] partitionKeys, string eTag = null)
    {
        _operations.Add(BatchOperation<T>.Delete(_operations.Count, id, partitionKeys, eTag));
        return this;
    }

    /// <summary>Executes ordered partition pipelines with bounded cross-partition concurrency.</summary>
    public Task<BulkExecutionResult<T>> ExecuteAsync(BulkExecutionOptions options = null, CancellationToken cancellationToken = default)
        => _execute(_operations, options ?? new(), cancellationToken);
}

internal static class BatchOperationFactory
{
    internal static BatchOperation<T> CreatePatch<T>(
        int index,
        string id,
        string[] partitionKeys,
        JsonNamingPolicy namingPolicy,
        Action<PatchBuilder<T>> configurePatch,
        string eTag,
        Action<PatchConditionBuilder<T>> configureCondition) where T : ICosmicEntity
    {
        if (configurePatch is null)
            throw new UniverseException("A patch configuration is required.");

        PatchBuilder<T> patch = new(namingPolicy);
        configurePatch(patch);
        if (patch.Operations.Count == 0)
            throw new UniverseException("A patch must contain at least one operation.");

        PatchConditionBuilder<T> condition = null;
        if (configureCondition is not null)
        {
            condition = new(namingPolicy);
            configureCondition(condition);
        }

        return BatchOperation<T>.Patch(index, id, partitionKeys, patch.Operations, eTag, condition?.Build(), patch.PayloadSize);
    }
}

internal sealed record BatchOperation<T>(
    int Index,
    BatchOperationKind Kind,
    string Id,
    string[] PartitionKeys,
    T Model,
    string ETag,
    IReadOnlyList<PatchOperation> PatchOperations,
    string FilterPredicate,
    int PatchPayloadSize) where T : ICosmicEntity
{
    internal static BatchOperation<T> Create(int index, T model, string[] partitionKeys)
    {
        if (model is null)
            throw new UniverseException("A batch model is required.");
        if (string.IsNullOrWhiteSpace(model.id))
            model.id = Guid.CreateVersion7().ToString();

        return new(index, BatchOperationKind.Create, model.id, ValidatePartitionKeys(partitionKeys), model, null, null, null, 0);
    }

    internal static BatchOperation<T> Replace(int index, T model, string[] partitionKeys, string eTag)
        => new(index, BatchOperationKind.Replace, RequireId(model.id), ValidatePartitionKeys(partitionKeys), model, eTag, null, null, 0);

    internal static BatchOperation<T> Patch(int index, string id, string[] partitionKeys, IReadOnlyList<PatchOperation> patchOperations, string eTag, string filterPredicate, int patchPayloadSize)
        => new(index, BatchOperationKind.Patch, RequireId(id), ValidatePartitionKeys(partitionKeys), default, eTag, patchOperations, filterPredicate, patchPayloadSize);

    internal static BatchOperation<T> Delete(int index, string id, string[] partitionKeys, string eTag)
        => new(index, BatchOperationKind.Delete, RequireId(id), ValidatePartitionKeys(partitionKeys), default, eTag, null, null, 0);

    internal void PrepareForExecution()
    {
        if (Kind is BatchOperationKind.Create)
        {
            Model.AddedOn = DateTime.UtcNow;
        }
        else if (Kind is BatchOperationKind.Replace)
        {
            Model.ModifiedOn = DateTime.UtcNow;
        }
    }

    internal int EstimatePayloadSize()
    {
        if (Kind is BatchOperationKind.Patch)
            return PatchPayloadSize + Encoding.UTF8.GetByteCount(Id) + Encoding.UTF8.GetByteCount(ETag ?? string.Empty) + Encoding.UTF8.GetByteCount(FilterPredicate ?? string.Empty);

        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(Model)) + Encoding.UTF8.GetByteCount(Id ?? string.Empty) + Encoding.UTF8.GetByteCount(ETag ?? string.Empty);
    }

    internal PartitionKey ToPartitionKey()
    {
        PartitionKeyBuilder builder = new();
        foreach (string partitionKey in PartitionKeys)
            builder.Add(partitionKey);
        return builder.Build();
    }

    private static string RequireId(string id)
        => string.IsNullOrWhiteSpace(id) ? throw new UniverseException("A batch operation id is required.") : id;

    private static string[] ValidatePartitionKeys(string[] partitionKeys)
    {
        if (partitionKeys is null || partitionKeys.Length == 0)
            throw new UniverseException("Partition key cannot be null or empty.");
        if (partitionKeys.Any(string.IsNullOrWhiteSpace))
            throw new UniverseException("Partition key value cannot be null or empty.");
        return [.. partitionKeys];
    }
}

internal sealed record BatchChunkResult<T>(
    bool Succeeded,
    HttpStatusCode StatusCode,
    double RU,
    IReadOnlyList<BatchOperationResult<T>> Operations) where T : ICosmicEntity;

internal static class TransactionalBatchExecutor<T> where T : ICosmicEntity
{
    internal static async Task<BatchChunkResult<T>> ExecuteAsync(Container container, IReadOnlyList<BatchOperation<T>> operations, CancellationToken cancellationToken)
    {
        if (operations.Count == 0)
            throw new UniverseException("A batch must contain at least one operation.");

        TransactionalBatch batch = container.CreateTransactionalBatch(operations[0].ToPartitionKey());
        foreach (BatchOperation<T> operation in operations)
            AddOperation(batch, operation);

        try
        {
            using TransactionalBatchResponse response = await batch.ExecuteAsync(cancellationToken);
            List<BatchOperationResult<T>> results = new(operations.Count);
            for (int index = 0; index < operations.Count; index++)
            {
                BatchOperation<T> operation = operations[index];
                TransactionalBatchOperationResult operationResponse = response.Count > index ? response[index] : null;
                HttpStatusCode status = operationResponse?.StatusCode ?? response.StatusCode;
                bool succeeded = operationResponse?.IsSuccessStatusCode ?? response.IsSuccessStatusCode;
                results.Add(new(
                    operation.Index,
                    operation.Kind,
                    operation.Id,
                    operation.PartitionKeys,
                    status,
                    operationResponse?.ETag,
                    // The SDK exposes request charge only at the batch level. Equal allocation keeps per-operation
                    // results additive without inferring unavailable service-side charges.
                    response.RequestCharge / operations.Count,
                    response.IsSuccessStatusCode && succeeded,
                    operation.Kind is BatchOperationKind.Create or BatchOperationKind.Replace ? operation.Model : default));
            }

            return new(response.IsSuccessStatusCode, response.StatusCode, response.RequestCharge, results);
        }
        catch (CosmosException ex)
        {
            throw new UniverseException($"A Cosmos DB error occurred. Status: {(int)ex.StatusCode}", ex);
        }
    }

    private static void AddOperation(TransactionalBatch batch, BatchOperation<T> operation)
    {
        TransactionalBatchItemRequestOptions itemOptions = new()
        {
            EnableContentResponseOnWrite = false,
            IfMatchEtag = operation.ETag
        };

        switch (operation.Kind)
        {
            case BatchOperationKind.Create:
                batch.CreateItem(operation.Model, new() { EnableContentResponseOnWrite = false });
                break;
            case BatchOperationKind.Replace:
                batch.ReplaceItem(operation.Id, operation.Model, itemOptions);
                break;
            case BatchOperationKind.Patch:
                batch.PatchItem(operation.Id, operation.PatchOperations, new()
                {
                    EnableContentResponseOnWrite = false,
                    IfMatchEtag = operation.ETag,
                    FilterPredicate = operation.FilterPredicate
                });
                break;
            case BatchOperationKind.Delete:
                batch.DeleteItem(operation.Id, itemOptions);
                break;
            default:
                throw new UniverseException("Unsupported batch operation.");
        }
    }
}
