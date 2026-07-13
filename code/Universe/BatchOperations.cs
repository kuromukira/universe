using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Universe.Response;

namespace Universe;

/// <summary>Operation kinds supported by Cosmos transactional batches.</summary>
public enum BatchOperationKind
{
    /// <summary>Creates a document.</summary>
    Create,
    /// <summary>Replaces an existing document.</summary>
    Replace,
    /// <summary>Applies JSON Patch operations to an existing document.</summary>
    Patch,
    /// <summary>Deletes an existing document.</summary>
    Delete
}

/// <summary>Result for one operation submitted through an atomic or bulk batch.</summary>
public sealed record BatchOperationResult<T>(
    int Index,
    BatchOperationKind Kind,
    string Id,
    IReadOnlyList<string> PartitionKeys,
    HttpStatusCode StatusCode,
    string ETag,
    double RU,
    bool Succeeded,
    T Resource = default);

/// <summary>Result of one same-partition transactional batch.</summary>
public sealed record AtomicBatchResult<T>(
    Gravity Gravity,
    bool Succeeded,
    HttpStatusCode StatusCode,
    IReadOnlyList<BatchOperationResult<T>> Operations);

/// <summary>Configures cross-partition bulk execution.</summary>
public sealed record BulkExecutionOptions
{
    /// <summary>Gets or sets the maximum number of logical partition pipelines executing at once.</summary>
    public int MaxConcurrency { get; init; } = 4;
}

/// <summary>Result of a cross-partition bulk execution.</summary>
public sealed record BulkExecutionResult<T>(
    Gravity Gravity,
    bool Succeeded,
    HttpStatusCode StatusCode,
    bool IsPartialSuccess,
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<BatchOperationResult<T>> Operations);

/// <summary>Builds typed Cosmos patch operations from property selectors.</summary>
public sealed class PatchBuilder<T>
{
    private const int MaxOperations = 10;
    private readonly JsonNamingPolicy _namingPolicy;
    private readonly List<PatchEntry> _entries = [];

    internal PatchBuilder(JsonNamingPolicy namingPolicy) => _namingPolicy = namingPolicy;

    /// <summary>Sets a property, creating it when necessary.</summary>
    public PatchBuilder<T> Set<TValue>(Expression<Func<T, TValue>> selector, TValue value)
        => Add(PatchOperation.Set(PathFor(selector), value), value);

    /// <summary>Adds a property or array element.</summary>
    public PatchBuilder<T> Add<TValue>(Expression<Func<T, TValue>> selector, TValue value)
        => Add(PatchOperation.Add(PathFor(selector), value), value);

    /// <summary>Replaces an existing property.</summary>
    public PatchBuilder<T> Replace<TValue>(Expression<Func<T, TValue>> selector, TValue value)
        => Add(PatchOperation.Replace(PathFor(selector), value), value);

    /// <summary>Removes an existing property.</summary>
    public PatchBuilder<T> Remove(Expression<Func<T, object>> selector)
        => Add(PatchOperation.Remove(PathFor(selector)), null);

    /// <summary>Increments an integral or floating-point property.</summary>
    public PatchBuilder<T> Increment<TValue>(Expression<Func<T, TValue>> selector, TValue value)
    {
        string path = PathFor(selector);
        return value switch
        {
            byte or sbyte or short or ushort or int or uint or long => Add(PatchOperation.Increment(path, Convert.ToInt64(value)), value),
            float or double or decimal => Add(PatchOperation.Increment(path, Convert.ToDouble(value)), value),
            _ => throw new UniverseException("Patch increments require an integral or floating-point value.")
        };
    }

    internal IReadOnlyList<PatchOperation> Operations => _entries.Select(entry => entry.Operation).ToArray();

    internal int PayloadSize => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(_entries.Select(entry => new
    {
        entry.Operation.OperationType,
        entry.Operation.Path,
        entry.Value
    })));

    private PatchBuilder<T> Add(PatchOperation operation, object value)
    {
        if (_entries.Count >= MaxOperations)
            throw new UniverseException($"Cosmos patch operations are limited to {MaxOperations} per item.");

        _entries.Add(new(operation, value));
        return this;
    }

    private string PathFor<TValue>(Expression<Func<T, TValue>> selector)
        => string.Join('/', [string.Empty, .. SelectorPath.Resolve(selector, _namingPolicy).Select(EscapePointerSegment)]);

    private static string EscapePointerSegment(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private sealed record PatchEntry(PatchOperation Operation, object Value);
}

/// <summary>Builds an injection-safe conditional predicate for a patch operation.</summary>
public sealed class PatchConditionBuilder<T>
{
    private readonly JsonNamingPolicy _namingPolicy;
    private readonly List<string> _clauses = [];
    private string _nextConnector;

    internal PatchConditionBuilder(JsonNamingPolicy namingPolicy) => _namingPolicy = namingPolicy;

    /// <summary>Connects the next predicate with AND.</summary>
    public PatchConditionBuilder<T> And() => SetConnector("AND");

    /// <summary>Connects the next predicate with OR.</summary>
    public PatchConditionBuilder<T> Or() => SetConnector("OR");

    /// <summary>Adds an equality predicate.</summary>
    public PatchConditionBuilder<T> Equal<TValue>(Expression<Func<T, TValue>> selector, TValue value) => Add(selector, "=", value);

    /// <summary>Adds an inequality predicate.</summary>
    public PatchConditionBuilder<T> NotEqual<TValue>(Expression<Func<T, TValue>> selector, TValue value) => Add(selector, "!=", value);

    /// <summary>Adds a greater-than predicate.</summary>
    public PatchConditionBuilder<T> GreaterThan<TValue>(Expression<Func<T, TValue>> selector, TValue value) => Add(selector, ">", value);

    /// <summary>Adds a greater-than-or-equal predicate.</summary>
    public PatchConditionBuilder<T> GreaterThanOrEqual<TValue>(Expression<Func<T, TValue>> selector, TValue value) => Add(selector, ">=", value);

    /// <summary>Adds a less-than predicate.</summary>
    public PatchConditionBuilder<T> LessThan<TValue>(Expression<Func<T, TValue>> selector, TValue value) => Add(selector, "<", value);

    /// <summary>Adds a less-than-or-equal predicate.</summary>
    public PatchConditionBuilder<T> LessThanOrEqual<TValue>(Expression<Func<T, TValue>> selector, TValue value) => Add(selector, "<=", value);

    internal string Build()
    {
        if (_clauses.Count == 0)
            return null;

        if (_nextConnector is not null)
            throw new UniverseException("A patch condition connector must be followed by a condition.");

        return $"FROM c WHERE {string.Join(' ', _clauses)}";
    }

    private PatchConditionBuilder<T> SetConnector(string connector)
    {
        if (_clauses.Count == 0 || _nextConnector is not null)
            throw new UniverseException("AND and OR must appear between patch conditions.");

        _nextConnector = connector;
        return this;
    }

    private PatchConditionBuilder<T> Add<TValue>(Expression<Func<T, TValue>> selector, string comparison, TValue value)
    {
        if (_clauses.Count > 0 && _nextConnector is null)
            throw new UniverseException("Patch conditions must be explicitly connected with And() or Or().");

        string literal = SerializeScalar(value);
        string property = $"c{string.Concat(SelectorPath.Resolve(selector, _namingPolicy).Select(segment => $"[{JsonSerializer.Serialize(segment)}]"))}";
        string clause = $"{property} {comparison} {literal}";
        _clauses.Add(_nextConnector is null ? clause : $"{_nextConnector} {clause}");
        _nextConnector = null;
        return this;
    }

    private static string SerializeScalar<TValue>(TValue value)
    {
        string json = JsonSerializer.Serialize(value);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            throw new UniverseException("Patch condition values must be JSON scalars.");

        return json;
    }
}

internal static class SelectorPath
{
    internal static IReadOnlyList<string> Resolve(LambdaExpression selector, JsonNamingPolicy namingPolicy)
    {
        if (selector is null)
            throw new UniverseException("A property selector is required.");

        Expression expression = selector.Body;
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            expression = unary.Operand;

        Stack<PropertyInfo> properties = new();
        while (expression is MemberExpression member)
        {
            if (member.Member is not PropertyInfo property)
                throw new UniverseException("Property selectors cannot target fields.");

            properties.Push(property);
            expression = member.Expression;
        }

        if (expression != selector.Parameters[0] || properties.Count == 0)
            throw new UniverseException("Only direct nested property selectors are supported for patches and conditions.");

        return properties.Select(property => ResolveName(property, namingPolicy)).ToArray();
    }

    private static string ResolveName(PropertyInfo property, JsonNamingPolicy namingPolicy)
    {
        string name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? namingPolicy?.ConvertName(property.Name) ?? property.Name;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl))
            throw new UniverseException("Patch and condition property names must be non-empty safe JSON names.");

        return name;
    }
}
