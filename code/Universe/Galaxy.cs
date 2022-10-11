using System.Net;
using Universe.Response;

namespace Universe;

/// <summary>Inherit repositories to implement Universe</summary>
public abstract class Galaxy<T> : IDisposable, IGalaxy<T> where T : ICosmicEntity
{
    private readonly Container Container;
    private bool DisposedValue;

    private bool RecordQuery;

    /// <summary></summary>
    protected Galaxy(Database db, string container, string partitionKey, bool recordQueries = false)
    {
        if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(partitionKey))
            throw new UniverseException("Container name and PartitionKey are required");

        RecordQuery = recordQueries;
        Container = db.CreateContainerIfNotExistsAsync(container, partitionKey).GetAwaiter().GetResult();
    }

    private static QueryDefinition CreateQuery(IList<Catalyst> catalysts, IList<string> columns = null, IList<Sorting.Option> sorting = null)
    {
        // Column Builder
        string columnsBuilder = "*";
        if (columns is not null && columns.Any())
            columnsBuilder = string.Join(", ", columns.Select(c => $"c.{c}").ToList());

        // Where Clause Builder
        StringBuilder queryBuilder = new($"SELECT {columnsBuilder} FROM c");
        if (catalysts.Any())
        {
            queryBuilder.Append($" WHERE {WhereClauseBuilder(catalysts[0])}");
            foreach (Catalyst catalyst in catalysts.Where(p => p.Column != catalysts[0].Column).ToList())
                queryBuilder.Append($" {catalyst.Where.Value()} {WhereClauseBuilder(catalyst)}");
        }

        // Sorting Builder
        if (sorting is not null && sorting.Any())
        {
            queryBuilder.Append($" ORDER BY c.{sorting[0].Column} {sorting[0].Direction.Value()}");
            foreach (Sorting.Option sort in sorting.Where(s => s.Column != sorting[0].Column).ToList())
                queryBuilder.Append($", c.{sort.Column} {sort.Direction.Value()}");
        }

        // Parameters Builder
        QueryDefinition query = new(queryBuilder.ToString());
        if (!catalysts.Any()) return query;
        {
            query = query.WithParameter($"@{catalysts[0].ParameterName()}", catalysts[0].Value);
            foreach (Catalyst catalyst in catalysts.Where(p => p.Column != catalysts[0].Column).ToList())
                query = query.WithParameter($"@{catalyst.ParameterName()}", catalyst.Value);
        }

        return query;

        static string WhereClauseBuilder(Catalyst catalyst) => catalyst.Operator switch
        {
            Q.Operator.In => $"ARRAY_CONTAINS(c.{catalyst.Column}, @{catalyst.ParameterName()})",
            Q.Operator.NotIn => $"NOT ARRAY_CONTAINS(c.{catalyst.Column}, @{catalyst.ParameterName()})",
            _ => $"c.{catalyst.Column} {catalyst.Operator.Value()} @{catalyst.ParameterName()}",
        };
    }

    async Task<(Gravity, string)> IGalaxy<T>.Create(T model)
    {
        if (string.IsNullOrWhiteSpace(model.id))
            model.id = Guid.NewGuid().ToString();
        model.AddedOn = DateTime.UtcNow;

        ItemResponse<T> response = await Container.CreateItemAsync(model, new PartitionKey(model.PartitionKey));
        return (new(response.RequestCharge, null), model.id);
    }

    async Task<Gravity> IGalaxy<T>.Create(IList<Catalyst> catalysts, T model)
    {
        QueryDefinition query = CreateQuery(catalysts: catalysts);

        using FeedIterator<T> queryResponse = Container.GetItemQueryIterator<T>(query);
        if (queryResponse.HasMoreResults)
        {
            FeedResponse<T> next = await queryResponse.ReadNextAsync();
            if (next.Count > 0)
                throw new UniverseException($"{typeof(T).Name} already exists.");
        }

        model.id = Guid.NewGuid().ToString();
        model.AddedOn = DateTime.UtcNow;

        ItemResponse<T> response = await Container.CreateItemAsync(model, new PartitionKey(model.PartitionKey));
        return new(response.RequestCharge, null, RecordQuery ? (query.QueryText, query.GetQueryParameters()) : default);
    }

    async Task<(Gravity, T)> IGalaxy<T>.Modify(T model)
    {
        try
        {
            ItemResponse<T> response = await Container.ReadItemAsync<T>(model.id, new PartitionKey(model.PartitionKey));
            model.ModifiedOn = DateTime.UtcNow;

            response = await Container.ReplaceItemAsync(model, response.Resource.id, new PartitionKey(response.Resource.PartitionKey));
            return (new(response.RequestCharge, null), response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<Gravity> IGalaxy<T>.Remove(string id, string partitionKey)
    {
        try
        {
            ItemResponse<T> response = await Container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey));
            return new(response.RequestCharge, null);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, T)> IGalaxy<T>.Get(string id, string partitionKey)
    {
        try
        {
            ItemResponse<T> response = await Container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
            return (new(response.RequestCharge, null), response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, T)> GetOneFromQuery(QueryDefinition query)
    {
        using FeedIterator<T> queryResponse = Container.GetItemQueryIterator<T>(query);
        if (queryResponse.HasMoreResults)
        {
            FeedResponse<T> next = await queryResponse.ReadNextAsync();
            return (new(next.RequestCharge, null, RecordQuery ? (query.QueryText, query.GetQueryParameters()) : default), next.Any() ? next.Resource.FirstOrDefault() : default);
        }
        else return new(new(0, null), default);
    }

    async Task<(Gravity, T)> IGalaxy<T>.Get(Catalyst catalyst, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(catalysts: new[] { catalyst }, columns: columns);
            return await GetOneFromQuery(query);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, T)> IGalaxy<T>.Get(IList<Catalyst> catalysts, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(catalysts: catalysts, columns: columns);
            return await GetOneFromQuery(query);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"{typeof(T).Name} does not exist.");
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, IList<T>)> GetListFromQuery(QueryDefinition query)
    {
        double requestCharge = 0;
        List<T> collection = new();
        using FeedIterator<T> queryResponse = Container.GetItemQueryIterator<T>(query);
        while (queryResponse.HasMoreResults)
        {
            FeedResponse<T> next = await queryResponse.ReadNextAsync();
            collection.AddRange(next);
            requestCharge += next.RequestCharge;
        }

        return (new(requestCharge, null, RecordQuery ? (query.QueryText, query.GetQueryParameters()) : default), collection);
    }

    async Task<(Gravity, IList<T>)> IGalaxy<T>.List(Catalyst catalyst, IList<string> columns, IList<Sorting.Option> sorting)
    {
        try
        {
            QueryDefinition query = CreateQuery(catalysts: new[] { catalyst }, columns: columns);
            return await GetListFromQuery(query);
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, IList<T>)> IGalaxy<T>.List(IList<Catalyst> catalysts, IList<string> columns, IList<Sorting.Option> sorting)
    {
        try
        {
            QueryDefinition query = CreateQuery(catalysts: catalysts, columns: columns);
            return await GetListFromQuery(query);
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, IList<T>)> IGalaxy<T>.Paged(Q.Page page, IList<Catalyst> catalysts, IList<string> columns, IList<Sorting.Option> sorting)
    {
        try
        {
            QueryDefinition query = CreateQuery(catalysts: catalysts, columns: columns);

            double requestUnit = 0;
            string continuationToken = string.Empty;
            List<T> collection = new();
            using FeedIterator<T> queryResponse = Container.GetItemQueryIterator<T>(query,
                requestOptions: new() { MaxItemCount = page.Size },
                continuationToken: string.IsNullOrWhiteSpace(page.ContinuationToken) ? null : page.ContinuationToken
            );
            while (queryResponse.HasMoreResults)
            {
                FeedResponse<T> next = await queryResponse.ReadNextAsync();
                collection.AddRange(next);
                requestUnit += next.RequestCharge;
                if (next.Count > 0)
                {
                    continuationToken = next.ContinuationToken;
                    break;
                }
            }

            return (new(requestUnit, continuationToken, RecordQuery ? (query.QueryText, query.GetQueryParameters()) : default), collection);
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    /// <summary></summary>
    protected virtual void Dispose(bool disposing)
    {
        if (DisposedValue) return;
        if (disposing)
        {
        }

        DisposedValue = true;
    }

    /// <summary></summary>
    ~Galaxy() => Dispose(disposing: false);

    /// <summary></summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
