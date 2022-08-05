using System.Net;

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
                switch(parameter.Operator)
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

    async Task<string> IGalaxy<T>.Create(T model)
    {
        if (string.IsNullOrWhiteSpace(model.id))
            model.id = Guid.NewGuid().ToString();
        model.AddedOn = DateTime.UtcNow;

        _ = await Container.CreateItemAsync(model, new PartitionKey(model.PartitionKey));
        return model.id;
    }

    async Task IGalaxy<T>.Create(IList<QueryParameter> parameters, T model)
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

        _ = await Container.CreateItemAsync(model, new PartitionKey(model.PartitionKey));
    }

    async Task<T> IGalaxy<T>.Modify(T model)
    {
        try
        {
            ItemResponse<T> response = await Container.ReadItemAsync<T>(model.id, new PartitionKey(model.PartitionKey));
            model.ModifiedOn = DateTime.UtcNow;

            response = await Container.ReplaceItemAsync(model, response.Resource.id, new PartitionKey(response.Resource.PartitionKey));
            return response.Resource;
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

    async Task IGalaxy<T>.Remove(string id, string partitionKey)
    {
        try
        {
            _ = await Container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey));
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

    async Task<T> IGalaxy<T>.Get(QueryParameter parameter, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(parameters: new[] { parameter }, columns: columns);

            using FeedIterator<T> queryResponse = Container.GetItemQueryIterator<T>(query);
            if (queryResponse.HasMoreResults)
            {
                FeedResponse<T> next = await queryResponse.ReadNextAsync();
                return next.Any() ? next.Resource.FirstOrDefault() : default;
            }
            else return default;
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

    async Task<T> IGalaxy<T>.Get(string id, string partitionKey)
    {
        try
        {
            ItemResponse<T> response = await Container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
            return response.Resource;
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

    async Task<IList<T>> IGalaxy<T>.Get(IList<QueryParameter> parameters, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(columns: columns, parameters: parameters);
            List<T> collection = new();
            using FeedIterator<T> queryResponse = Container.GetItemQueryIterator<T>(query);
            while (queryResponse.HasMoreResults)
            {
                FeedResponse<T> next = await queryResponse.ReadNextAsync();
                collection.AddRange(next);
            }

            return collection;
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
