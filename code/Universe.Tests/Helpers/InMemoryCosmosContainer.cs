using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Universe.Extensions;
using Universe.Interfaces;

namespace Universe.Tests.Helpers;

internal sealed class InMemoryCosmosContainer<T> : Container where T : class, ICosmicEntity
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();
    private readonly Dictionary<string, StoredDocument> _documents = [];
    private readonly ConcurrentQueue<System.Exception> _transportFailures = [];
    private int _activeExecutions;
    private int _etagSequence;
    private int _maxConcurrentExecutions;
    private int _readItemCalls;

    internal List<InMemoryBatchExecution<T>> Executions { get; } = [];
    internal TimeSpan ExecutionDelay { get; set; }
    internal Func<T, string, bool> PatchConditionEvaluator { get; set; }
    internal int MaxConcurrentExecutions => _maxConcurrentExecutions;
    internal int ReadItemCalls => _readItemCalls;

    public override string Id => "in-memory-container";
    public override Database Database => throw new NotSupportedException();
    public override Conflicts Conflicts => throw new NotSupportedException();
    public override Scripts Scripts => throw new NotSupportedException();

    internal void Seed(T model, string eTag = "etag-seed")
    {
        ArgumentNullException.ThrowIfNull(model);
        lock (_sync)
            _documents[DocumentKey(model.BuildPartitionKey().ToString(), model.id)] = new(Clone(model), eTag);
    }

    internal (T Model, string ETag)? Find(T model)
        => Find(model.id, model.BuildPartitionKey().ToString());

    internal (T Model, string ETag)? Find(string id, string partitionKey)
    {
        lock (_sync)
        {
            return _documents.TryGetValue(DocumentKey(partitionKey, id), out StoredDocument stored)
                ? (Clone(stored.Model), stored.ETag)
                : null;
        }
    }

    internal void FailNextExecution(System.Exception exception) => _transportFailures.Enqueue(exception);

    public override TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey)
        => new InMemoryTransactionalBatch<T>(this, partitionKey.ToString());

    internal async Task<TransactionalBatchResponse> ExecuteAsync(
        string partitionKey,
        IReadOnlyList<InMemoryBatchOperation<T>> operations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int active = Interlocked.Increment(ref _activeExecutions);
        UpdateMaxConcurrency(active);
        try
        {
            if (ExecutionDelay > TimeSpan.Zero)
                await Task.Delay(ExecutionDelay, cancellationToken);

            if (_transportFailures.TryDequeue(out System.Exception transportFailure))
                throw transportFailure;

            lock (_sync)
            {
                Dictionary<string, StoredDocument> snapshot = _documents.ToDictionary(
                    pair => pair.Key,
                    pair => new StoredDocument(Clone(pair.Value.Model), pair.Value.ETag));
                List<InMemoryOperationOutcome> outcomes = [];
                int failureIndex = -1;

                for (int index = 0; index < operations.Count; index++)
                {
                    InMemoryOperationOutcome outcome = Apply(snapshot, partitionKey, operations[index]);
                    outcomes.Add(outcome);
                    if (!outcome.IsSuccessStatusCode)
                    {
                        failureIndex = index;
                        break;
                    }
                }

                if (failureIndex >= 0)
                {
                    outcomes = operations.Select((_, index) => index == failureIndex
                        ? outcomes[failureIndex]
                        : new InMemoryOperationOutcome(HttpStatusCode.FailedDependency, null)).ToList();
                }
                else
                {
                    _documents.Clear();
                    foreach ((string key, StoredDocument value) in snapshot)
                        _documents[key] = value;
                }

                double requestCharge = operations.Count * 1.25;
                HttpStatusCode statusCode = failureIndex < 0 ? HttpStatusCode.OK : outcomes[failureIndex].StatusCode;
                InMemoryBatchExecution<T> execution = new(
                    partitionKey,
                    operations.ToArray(),
                    failureIndex < 0,
                    statusCode,
                    requestCharge,
                    outcomes.ToArray());
                Executions.Add(execution);
                return new InMemoryTransactionalBatchResponse(statusCode, requestCharge, outcomes);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeExecutions);
        }
    }

    public override Task<ItemResponse<TItem>> ReadItemAsync<TItem>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _readItemCalls);
        (T Model, string ETag)? found = Find(id, partitionKey.ToString());
        if (found is null)
            throw new CosmosException("Not found", HttpStatusCode.NotFound, 0, "in-memory", 0);

        return Task.FromResult<ItemResponse<TItem>>(new InMemoryItemResponse<TItem>(
            (TItem)(object)found.Value.Model,
            found.Value.ETag,
            HttpStatusCode.OK,
            1));
    }

    private InMemoryOperationOutcome Apply(
        Dictionary<string, StoredDocument> snapshot,
        string partitionKey,
        InMemoryBatchOperation<T> operation)
    {
        string key = DocumentKey(partitionKey, operation.Id);
        snapshot.TryGetValue(key, out StoredDocument current);

        if (operation.Kind is InMemoryBatchOperationKind.Create)
        {
            if (current is not null)
                return new(HttpStatusCode.Conflict, null);

            string eTag = NextETag();
            snapshot[key] = new(Clone(operation.Model), eTag);
            return new(HttpStatusCode.Created, eTag);
        }

        if (current is null)
            return new(HttpStatusCode.NotFound, null);
        if (!string.IsNullOrWhiteSpace(operation.IfMatchETag) && operation.IfMatchETag != current.ETag)
            return new(HttpStatusCode.PreconditionFailed, current.ETag);
        if (operation.RequestOptions is TransactionalBatchItemRequestOptions requestOptions &&
            !string.IsNullOrWhiteSpace(requestOptions.IfNoneMatchEtag))
            return new(HttpStatusCode.BadRequest, current.ETag);
        if (operation.Kind is InMemoryBatchOperationKind.Patch && !string.IsNullOrWhiteSpace(operation.FilterPredicate))
        {
            if (PatchConditionEvaluator is null)
                return new(HttpStatusCode.BadRequest, current.ETag);
            if (!PatchConditionEvaluator(Clone(current.Model), operation.FilterPredicate))
                return new(HttpStatusCode.PreconditionFailed, current.ETag);
        }

        if (operation.Kind is InMemoryBatchOperationKind.Delete)
        {
            snapshot.Remove(key);
            return new(HttpStatusCode.NoContent, null);
        }

        string nextETag = NextETag();
        T next;
        try
        {
            next = operation.Kind is InMemoryBatchOperationKind.Replace
                ? Clone(operation.Model)
                : ApplyPatch(current.Model, operation.PatchOperations);
        }
        catch (InvalidOperationException)
        {
            return new(HttpStatusCode.BadRequest, current.ETag);
        }
        catch (NotSupportedException)
        {
            return new(HttpStatusCode.BadRequest, current.ETag);
        }
        snapshot[key] = new(next, nextETag);
        return new(HttpStatusCode.OK, nextETag);
    }

    private static T ApplyPatch(T model, IReadOnlyList<PatchOperation> operations)
    {
        JsonNode root = JsonSerializer.SerializeToNode(model, SerializerOptions)
            ?? throw new InvalidOperationException("Unable to serialize an in-memory document.");

        foreach (PatchOperation operation in operations)
        {
            string[] segments = operation.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))
                .ToArray();
            JsonObject parent = NavigateToParent(root, segments);
            string property = segments[^1];
            object value = PatchValue(operation);

            switch (operation.OperationType)
            {
                case PatchOperationType.Add:
                case PatchOperationType.Set:
                    parent[property] = JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object));
                    break;
                case PatchOperationType.Replace:
                    if (!parent.ContainsKey(property))
                        throw new InvalidOperationException($"Patch replace target '{operation.Path}' does not exist.");
                    parent[property] = JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object));
                    break;
                case PatchOperationType.Remove:
                    if (!parent.Remove(property))
                        throw new InvalidOperationException($"Patch remove target '{operation.Path}' does not exist.");
                    break;
                case PatchOperationType.Increment:
                    parent[property] = parent.TryGetPropertyValue(property, out JsonNode current)
                        ? Increment(current, value)
                        : JsonSerializer.SerializeToNode(value, value.GetType());
                    break;
                default:
                    throw new NotSupportedException($"Patch operation '{operation.OperationType}' is not supported by the in-memory harness.");
            }
        }

        return root.Deserialize<T>(SerializerOptions)
            ?? throw new InvalidOperationException("Unable to deserialize an in-memory document.");
    }

    private static JsonObject NavigateToParent(JsonNode root, IReadOnlyList<string> segments)
    {
        JsonNode current = root;
        for (int index = 0; index < segments.Count - 1; index++)
        {
            current = current[segments[index]]
                ?? throw new InvalidOperationException($"Patch path segment '{segments[index]}' does not exist.");
        }

        return current as JsonObject
            ?? throw new InvalidOperationException("Patch parent must be a JSON object.");
    }

    private static JsonNode Increment(JsonNode current, object increment)
    {
        if (current is null || increment is null)
            throw new InvalidOperationException("Increment requires numeric values.");
        if (current is not JsonValue numeric)
            throw new InvalidOperationException("Increment requires a supported numeric value.");

        if (numeric.TryGetValue<int>(out int intValue))
            return JsonValue.Create(intValue + Convert.ToInt32(increment));
        if (numeric.TryGetValue<long>(out long longValue))
            return JsonValue.Create(longValue + Convert.ToInt64(increment));
        if (numeric.TryGetValue<double>(out double doubleValue))
            return JsonValue.Create(doubleValue + Convert.ToDouble(increment));

        throw new InvalidOperationException("Increment requires a supported numeric value.");
    }

    private static object PatchValue(PatchOperation operation)
    {
        for (Type type = operation.GetType(); type is not null; type = type.BaseType)
        {
            System.Reflection.PropertyInfo property = type.GetProperty(
                "Value",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly);
            if (property?.GetValue(operation) is object propertyValue)
                return propertyValue;

            foreach (System.Reflection.FieldInfo field in type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (field.Name.Contains("value", StringComparison.OrdinalIgnoreCase) && field.GetValue(operation) is object fieldValue)
                    return fieldValue;
            }
        }

        string members = string.Join(", ", operation.GetType()
            .GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Select(member => $"{member.MemberType}:{member.Name}"));
        throw new InvalidOperationException($"Unable to read patch value from {operation.GetType().FullName}. Members: {members}");
    }

    private void UpdateMaxConcurrency(int active)
    {
        int observed;
        do
        {
            observed = _maxConcurrentExecutions;
            if (active <= observed)
                return;
        }
        while (Interlocked.CompareExchange(ref _maxConcurrentExecutions, active, observed) != observed);
    }

    private string NextETag() => $"etag-{Interlocked.Increment(ref _etagSequence)}";
    private static string DocumentKey(string partitionKey, string id) => $"{partitionKey}\u001f{id}";
    private static T Clone(T model) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(model, SerializerOptions), SerializerOptions);

    private sealed record StoredDocument(T Model, string ETag);

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
    public override Task<ItemResponse<TItem>> CreateItemAsync<TItem>(TItem item, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<ItemResponse<TItem>> UpsertItemAsync<TItem>(TItem item, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<ResponseMessage> ReplaceItemStreamAsync(Stream streamPayload, string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<ItemResponse<TItem>> ReplaceItemAsync<TItem>(TItem item, string id, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<FeedResponse<TItem>> ReadManyItemsAsync<TItem>(IReadOnlyList<(string id, PartitionKey partitionKey)> items, ReadManyRequestOptions readManyRequestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<ResponseMessage> ReadManyItemsStreamAsync(IReadOnlyList<(string id, PartitionKey partitionKey)> items, ReadManyRequestOptions readManyRequestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<ItemResponse<TItem>> PatchItemAsync<TItem>(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<ResponseMessage> PatchItemStreamAsync(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<ItemResponse<TItem>> DeleteItemAsync<TItem>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<ResponseMessage> DeleteItemStreamAsync(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override FeedIterator GetItemQueryStreamIterator(QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override FeedIterator GetItemQueryStreamIterator(string queryText = null, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override FeedIterator<TItem> GetItemQueryIterator<TItem>(QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override FeedIterator<TItem> GetItemQueryIterator<TItem>(string queryText = null, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override FeedIterator GetItemQueryStreamIterator(FeedRange feedRange, QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override FeedIterator<TItem> GetItemQueryIterator<TItem>(FeedRange feedRange, QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override IOrderedQueryable<TItem> GetItemLinqQueryable<TItem>(bool allowSynchronousQueryExecution = false, string continuationToken = null, QueryRequestOptions requestOptions = null, CosmosLinqSerializerOptions linqSerializerOptions = null) => throw new NotSupportedException();
    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<TItem>(string processorName, ChangesHandler<TItem> onChangesDelegate) => throw new NotSupportedException();
    public override ChangeFeedProcessorBuilder GetChangeFeedEstimatorBuilder(string processorName, ChangesEstimationHandler estimationDelegate, TimeSpan? estimationPeriod = null) => throw new NotSupportedException();
    public override ChangeFeedEstimator GetChangeFeedEstimator(string processorName, Container leaseContainer) => throw new NotSupportedException();
    public override Task<IReadOnlyList<FeedRange>> GetFeedRangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override FeedIterator GetChangeFeedStreamIterator(ChangeFeedStartFrom changeFeedStartFrom, ChangeFeedMode changeFeedMode, ChangeFeedRequestOptions changeFeedRequestOptions = null) => throw new NotSupportedException();
    public override FeedIterator<TItem> GetChangeFeedIterator<TItem>(ChangeFeedStartFrom changeFeedStartFrom, ChangeFeedMode changeFeedMode, ChangeFeedRequestOptions changeFeedRequestOptions = null) => throw new NotSupportedException();
    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<TItem>(string processorName, ChangeFeedHandler<TItem> onChangesDelegate) => throw new NotSupportedException();
    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint<TItem>(string processorName, ChangeFeedHandlerWithManualCheckpoint<TItem> onChangesDelegate) => throw new NotSupportedException();
    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder(string processorName, ChangeFeedStreamHandler onChangesDelegate) => throw new NotSupportedException();
    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint(string processorName, ChangeFeedStreamHandlerWithManualCheckpoint onChangesDelegate) => throw new NotSupportedException();
}

internal sealed record InMemoryBatchExecution<T>(
    string PartitionKey,
    IReadOnlyList<InMemoryBatchOperation<T>> Operations,
    bool Succeeded,
    HttpStatusCode StatusCode,
    double RequestCharge,
    IReadOnlyList<InMemoryOperationOutcome> Outcomes) where T : class, ICosmicEntity;

internal enum InMemoryBatchOperationKind
{
    Create,
    Replace,
    Patch,
    Delete
}

internal sealed record InMemoryBatchOperation<T>(
    InMemoryBatchOperationKind Kind,
    string Id,
    T Model,
    string IfMatchETag,
    IReadOnlyList<PatchOperation> PatchOperations,
    string FilterPredicate,
    object RequestOptions) where T : class, ICosmicEntity;

internal sealed class InMemoryTransactionalBatch<T>(
    InMemoryCosmosContainer<T> container,
    string partitionKey) : TransactionalBatch where T : class, ICosmicEntity
{
    private readonly List<InMemoryBatchOperation<T>> _operations = [];

    public override TransactionalBatch CreateItem<TItem>(TItem item, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        T model = RequireModel(item);
        _operations.Add(new(InMemoryBatchOperationKind.Create, model.id, model, null, null, null, requestOptions));
        return this;
    }

    public override TransactionalBatch ReplaceItem<TItem>(string id, TItem item, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _operations.Add(new(InMemoryBatchOperationKind.Replace, id, RequireModel(item), requestOptions?.IfMatchEtag, null, null, requestOptions));
        return this;
    }

    public override TransactionalBatch PatchItem(
        string id,
        IReadOnlyList<PatchOperation> patchOperations,
        TransactionalBatchPatchItemRequestOptions requestOptions = null)
    {
        _operations.Add(new(InMemoryBatchOperationKind.Patch, id, null, requestOptions?.IfMatchEtag, patchOperations, requestOptions?.FilterPredicate, requestOptions));
        return this;
    }

    public override TransactionalBatch DeleteItem(string id, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _operations.Add(new(InMemoryBatchOperationKind.Delete, id, null, requestOptions?.IfMatchEtag, null, null, requestOptions));
        return this;
    }

    public override Task<TransactionalBatchResponse> ExecuteAsync(CancellationToken cancellationToken = default)
        => container.ExecuteAsync(partitionKey, _operations, cancellationToken);

    public override Task<TransactionalBatchResponse> ExecuteAsync(TransactionalBatchRequestOptions requestOptions, CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);

    private static T RequireModel<TItem>(TItem item)
        => item as T ?? throw new InvalidOperationException($"Expected a {typeof(T).Name} batch item.");

    public override TransactionalBatch CreateItemStream(Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override TransactionalBatch ReadItem(string id, TransactionalBatchItemRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override TransactionalBatch UpsertItem<TItem>(TItem item, TransactionalBatchItemRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override TransactionalBatch UpsertItemStream(Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null) => throw new NotSupportedException();
    public override TransactionalBatch ReplaceItemStream(string id, Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null) => throw new NotSupportedException();
}

internal sealed record InMemoryOperationOutcome(HttpStatusCode StatusCode, string ETag)
{
    internal bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

internal sealed class InMemoryTransactionalBatchResponse(
    HttpStatusCode statusCode,
    double requestCharge,
    IReadOnlyList<InMemoryOperationOutcome> outcomes) : TransactionalBatchResponse
{
    public override bool IsSuccessStatusCode => (int)statusCode is >= 200 and <= 299;
    public override HttpStatusCode StatusCode => statusCode;
    public override double RequestCharge => requestCharge;
    public override int Count => outcomes.Count;
    public override TransactionalBatchOperationResult this[int index] => new InMemoryTransactionalBatchOperationResult(outcomes[index]);
    public override IEnumerator<TransactionalBatchOperationResult> GetEnumerator()
        => outcomes.Select(outcome => new InMemoryTransactionalBatchOperationResult(outcome)).GetEnumerator();
}

internal sealed class InMemoryTransactionalBatchOperationResult(
    InMemoryOperationOutcome outcome) : TransactionalBatchOperationResult
{
    public override HttpStatusCode StatusCode => outcome.StatusCode;
    public override bool IsSuccessStatusCode => outcome.IsSuccessStatusCode;
    public override string ETag => outcome.ETag;
}

internal sealed class InMemoryItemResponse<TItem>(
    TItem resource,
    string eTag,
    HttpStatusCode statusCode,
    double requestCharge) : ItemResponse<TItem>
{
    public override TItem Resource => resource;
    public override string ETag => eTag;
    public override HttpStatusCode StatusCode => statusCode;
    public override double RequestCharge => requestCharge;
}
