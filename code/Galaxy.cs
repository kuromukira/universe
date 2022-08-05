using System.Net;
using Universe.Response;

namespace Universe;

/// <summary>Inherit repositories to implement Universe</summary>
public abstract class Galaxy<T> : IDisposable, IGalaxy<T> where T : ICosmicEntity
{
    private readonly Container Container;
    private bool DisposedValue;

    /// <summary></summary>
    protected Galaxy(Database db, string container, string partitionKey)
        => Container = db.CreateContainerIfNotExistsAsync(container, partitionKey).GetAwaiter().GetResult();

    private static QueryDefinition CreateQuery(IList<QueryParameter> parameters, IList<string> columns = null)
    {
        StringBuilder columnBuilder = new StringBuilder();
        if (columns is not null && columns.Any())
            columnBuilder.Append(string.Join(", ", columns));
        else columnBuilder.Append("*");

        StringBuilder queryBuilder = new($"SELECT {columnBuilder} FROM c");
        if (parameters.Any())
        {
            queryBuilder.Append($" WHERE c.{parameters[0].Column} {parameters[0].Operator} @{parameters[0].Column}");
            foreach (QueryParameter parameter in parameters.Where(p => p.Column != parameters[0].Column).ToList())
            {
                switch (parameter.Operator)
                {
                    case Query.Operator.In:
                        queryBuilder.Append($" {parameter.Where} ARRAY_CONTAINS({$"c.{parameter.Column}"}, {$"@{parameter.Column}"})");
                        break;

                    case Query.Operator.Notin:
                        queryBuilder.Append($" {parameter.Where} NOT ARRAY_CONTAINS({$"c.{parameter.Column}"}, {$"@{parameter.Column}"})");
                        break;

                    default:
                        queryBuilder.Append($" {parameter.Where} {$"c.{parameter.Column}"} {parameter.Operator} {$"@{parameter.Column}"}");
                        break;

                }
            }
        }

        QueryDefinition query = new(queryBuilder.ToString());
        if (!parameters.Any()) return query;
        {
            query = query.WithParameter($"@{parameters[0].Column}", parameters[0].Value);
            foreach (QueryParameter parameter in parameters.Where(p => p.Column != parameters[0].Column).ToList())
                query = query.WithParameter($"@{parameter.Column}", parameter.Value);
        }

        return query;
    }

    async Task<(Gravity, string)> IGalaxy<T>.Create(T model)
    {
        if (string.IsNullOrWhiteSpace(model.id))
            model.id = Guid.NewGuid().ToString();
        model.AddedOn = DateTime.UtcNow;

        ItemResponse<T> response = await Container.CreateItemAsync(model, new PartitionKey(model.PartitionKey));
        return (new(response.RequestCharge, null), model.id);
    }

    async Task<Gravity> IGalaxy<T>.Create(IList<QueryParameter> parameters, T model)
    {
        QueryDefinition query = CreateQuery(parameters: parameters);

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
        return new(response.RequestCharge, null);
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
            return (new(next.RequestCharge, null), next.Any() ? next.Resource.FirstOrDefault() : default);
        }
        else return new(new(0, null), default);
    }

    async Task<(Gravity, T)> IGalaxy<T>.Get(QueryParameter parameter, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(parameters: new[] { parameter }, columns: columns);
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

    async Task<(Gravity, T)> IGalaxy<T>.Get(IList<QueryParameter> parameters, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(parameters: parameters, columns: columns);
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

        return (new(requestCharge, null), collection);
    }

    async Task<(Gravity, IList<T>)> IGalaxy<T>.List(QueryParameter parameter, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(columns: columns, parameters: new[] { parameter });
            return await GetListFromQuery(query);
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, IList<T>)> IGalaxy<T>.List(IList<QueryParameter> parameters, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(columns: columns, parameters: parameters);
            return await GetListFromQuery(query);
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, IList<T>)> IGalaxy<T>.Paged(Query.Page page, IList<QueryParameter> parameters, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(columns: columns, parameters: parameters);

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

            return (new(requestUnit, continuationToken), collection);
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
