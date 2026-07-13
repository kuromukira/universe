using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Universe.Builder.Caching;

internal readonly record struct DocumentCacheKey(string Value);

internal enum DocumentCacheOperation
{
    PointRead,
    SingleQuery
}

internal sealed class DocumentCache(DocumentCacheOptions options)
{
    private static readonly Regex ParameterNamePattern = new(@"@\w+", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        IgnoreReadOnlyFields = true,
        IgnoreReadOnlyProperties = true
    };

    private readonly ConcurrentDictionary<DocumentCacheKey, DocumentCacheEntry> _entries = [];
    private readonly ConcurrentQueue<DocumentCacheKey> _insertionOrder = [];

    internal string CreateScopeHash(string database, string container, Type sourceType)
        => HashParts(["scope", database, container, sourceType.AssemblyQualifiedName]);

    internal DocumentCacheKey CreatePointKey(
        string database,
        string container,
        Type sourceType,
        Type resultType,
        string id,
        IReadOnlyList<string> partitionKeys)
        => new(HashParts([
            "point",
            database,
            container,
            sourceType.AssemblyQualifiedName,
            resultType.AssemblyQualifiedName,
            id,
            .. partitionKeys
        ]));

    internal DocumentCacheKey CreateQueryKey(
        string database,
        string container,
        Type sourceType,
        Type resultType,
        string queryText,
        IReadOnlyList<(string Name, object Value)> parameters)
    {
        string normalizedQuery = ParameterNamePattern.Replace(queryText, "@p");
        List<string> parts =
        [
            "query",
            database,
            container,
            sourceType.AssemblyQualifiedName,
            resultType.AssemblyQualifiedName,
            normalizedQuery
        ];

        foreach ((_, object value) in parameters)
            parts.Add(SerializeForKey(value));

        return new(HashParts(parts));
    }

    internal bool TryGet<T>(DocumentCacheKey key, out T value)
        => TryGet(key, out value, out _);

    internal bool TryGet<T>(DocumentCacheKey key, out T value, out string eTag)
    {
        value = default;
        eTag = null;

        if (!_entries.TryGetValue(key, out DocumentCacheEntry entry))
            return false;

        if (DateTimeOffset.UtcNow - entry.CreatedOn >= options.TimeToLive)
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        try
        {
            value = CloneIfNeeded<T>(entry.Value);
            eTag = entry.ETag;
            return true;
        }
        catch (SystemException)
        {
            _entries.TryRemove(key, out _);
            value = default;
            return false;
        }
    }

    internal void Set(DocumentCacheKey key, DocumentCacheOperation operation, string scopeHash, object value, string eTag = null)
    {
        object valueToStore;
        try
        {
            valueToStore = options.CloneDocuments ? CloneIfNeeded<object>(value) : value;
        }
        catch (SystemException)
        {
            return;
        }

        _entries[key] = new(valueToStore, DateTimeOffset.UtcNow, operation, scopeHash, eTag);
        _insertionOrder.Enqueue(key);
        EnforceMaxEntries();
    }

    internal void Remove(DocumentCacheKey key) => _entries.TryRemove(key, out _);

    internal void ClearQueries(string scopeHash)
    {
        foreach (KeyValuePair<DocumentCacheKey, DocumentCacheEntry> entry in _entries)
        {
            if (entry.Value.ScopeHash == scopeHash && entry.Value.Operation is DocumentCacheOperation.SingleQuery)
                _entries.TryRemove(entry.Key, out _);
        }
    }

    private void EnforceMaxEntries()
    {
        while (_entries.Count > options.MaxEntries && _insertionOrder.TryDequeue(out DocumentCacheKey key))
            _entries.TryRemove(key, out _);
    }

    private T CloneIfNeeded<T>(object value)
    {
        if (!options.CloneDocuments || value is null)
            return (T)value;

        Type valueType = value.GetType();
        string json = JsonSerializer.Serialize(value, valueType, JsonOptions);
        return (T)JsonSerializer.Deserialize(json, valueType, JsonOptions);
    }

    private static string SerializeForKey(object value)
    {
        if (value is null)
            return "null";

        return JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
    }

    private static string HashParts(IEnumerable<string> parts)
    {
        string payload = JsonSerializer.Serialize(parts, JsonOptions);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record DocumentCacheEntry(
        object Value,
        DateTimeOffset CreatedOn,
        DocumentCacheOperation Operation,
        string ScopeHash,
        string ETag);
}
