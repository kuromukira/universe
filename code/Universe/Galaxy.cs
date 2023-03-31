using System.Net;
using Universe.Response;

namespace Universe;

/// <summary>Inherit repositories to implement Universe</summary>
public abstract class Galaxy<T> : IDisposable, IGalaxy<T> where T : class, ICosmicEntity
{
    private readonly Container _container;
    private bool _disposedValue;

    private readonly bool _recordQuery;
    private readonly bool _allowBulk;

    /// <summary></summary>
    protected Galaxy(CosmosClient client, string database, string container, string partitionKey, bool recordQueries = false)
    {
        if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(partitionKey))
            throw new UniverseException("Container name and PartitionKey are required");

        _recordQuery = recordQueries;
        if (client.ClientOptions is not null)
            _allowBulk = client.ClientOptions.AllowBulkExecution;
        _container = client.GetDatabase(database).CreateContainerIfNotExistsAsync(container, partitionKey).GetAwaiter().GetResult();
    }

    private static QueryDefinition CreateQuery(IList<Cluster> clusters, ColumnOptions? columnOptions = null, IList<Sorting.Option> sorting = null, IList<string> groups = null)
    {
        // Column Options Builder
        string columnsInQuery = "*";
        if (columnOptions is not null)
        {
            if (columnOptions.Value.Names is not null && columnOptions.Value.Names.Count > 0)
                columnsInQuery = string.Join(", ", columnOptions.Value.Names.Select(c => $"c.{c}").ToList());

            if ((columnOptions.Value.Top) > 0)
                columnsInQuery = $"TOP {columnOptions.Value.Top} {columnsInQuery}";

            if (columnOptions.Value.IsDistinct)
                columnsInQuery = $"DISTINCT {columnsInQuery}";

            if (columnOptions.Value.Count)
            {
                groups ??= new List<string>();
                groups = groups.Concat(columnOptions.Value.Names ?? new List<string>()).Distinct().ToList();
                columnsInQuery = $"{columnsInQuery}, COUNT(1) Count";
            }
        }

        // This error blocks code execution since this is not yet supported by CosmosDb
        if (sorting is not null && sorting.Any() && groups is not null && groups.Any())
            throw new UniverseException("ORDER BY is not supported in presence of GROUP BY");

        // Update Columns Builder with Group By
        if (columnsInQuery.Contains('*') && groups is not null && groups.Any())
            _ = columnsInQuery.Replace("*", string.Join(", ", groups.Select(c => $"c.{c}").ToList()));

        StringBuilder queryBuilder = new($"SELECT {columnsInQuery} FROM c");

        // Validate Clusters
        if (clusters is not null && clusters.Any(c => c.Catalysts is null || !c.Catalysts.Any()))
            throw new UniverseException("Catalysts inside of a Cluster must not be null or empty.");

        // Construct Where Clause by Clusters
        if (clusters is not null)
        {
            foreach (Cluster cluster in clusters)
            {
                // Validate Catalysts
                if (cluster.Catalysts.Any(c => c.RuleViolations().Any()))
                {
                    List<IEnumerable<string>> violationsPerCatalyst = cluster.Catalysts.Select(c => c.RuleViolations()).ToList();
                    List<string> violations = violationsPerCatalyst.SelectMany(v => v).ToList().Distinct().ToList();
                    throw new UniverseException(string.Join(Environment.NewLine, violations));
                }

                // Add the where statement if not yet present
                if (clusters.IndexOf(cluster) == 0)
                    queryBuilder.Append(" WHERE (");
                else queryBuilder.Append($" {cluster.Where.Value()} (");

                // Where Clause Builder
                foreach (Catalyst catalyst in cluster.Catalysts)
                {
                    if (cluster.Catalysts.IndexOf(catalyst) == 0)
                        queryBuilder.Append(WhereClauseBuilder(catalyst));
                    else queryBuilder.Append($" {catalyst.Where.Value()} {WhereClauseBuilder(catalyst)}");
                }

                queryBuilder.Append(')');
            }
        }

        // Sorting Builder
        if (sorting is not null && sorting.Any())
        {
            queryBuilder.Append($" ORDER BY c.{sorting[0].Column} {sorting[0].Direction.Value()}");
            foreach (Sorting.Option sort in sorting.Where(s => s.Column != sorting[0].Column).ToList())
                queryBuilder.Append($", c.{sort.Column} {sort.Direction.Value()}");
        }

        // Group By Builder
        if (groups is not null && groups.Any())
        {
            queryBuilder.Append($" GROUP BY c.{groups[0]}");
            foreach (string group in groups.Where(g => g != groups[0]).ToList())
                queryBuilder.Append($", c.{group}");
        }

        // Parameters Builder
        QueryDefinition query = new(queryBuilder.ToString());

        if (clusters is not null && clusters.Any())
        {
            query = clusters.SelectMany(cluster => cluster.Catalysts)
                .Aggregate(query, (current, catalyst) =>
                    current.WithParameter($"@{catalyst.ParameterName()}", catalyst.Value));
        }

        return query;

        static string WhereClauseBuilder(Catalyst catalyst) => catalyst.Operator switch
        {
            Q.Operator.In => $"ARRAY_CONTAINS(c.{catalyst.Column}, @{catalyst.ParameterName()})",
            Q.Operator.NotIn => $"NOT ARRAY_CONTAINS(c.{catalyst.Column}, @{catalyst.ParameterName()})",
            Q.Operator.Defined => $"IS_DEFINED(c.{catalyst.Column})",
            Q.Operator.NotDefined => $"NOT IS_DEFINED(c.{catalyst.Column})",
            _ => $"c.{catalyst.Column} {catalyst.Operator.Value()} @{catalyst.ParameterName()}",
        };
    }

    async Task<(Gravity, string)> IGalaxy<T>.Create(T model)
    {
        if (string.IsNullOrWhiteSpace(model.id))
            model.id = Guid.NewGuid().ToString();
        model.AddedOn = DateTime.UtcNow;

        ItemResponse<T> response = await _container.CreateItemAsync(model, new PartitionKey(model.PartitionKey));
        return (new(response.RequestCharge, null), model.id);
    }

    async Task<Gravity> IGalaxy<T>.Create(IList<T> models)
    {
        if (!_allowBulk)
            throw new UniverseException("Bulk create of documents is not configured properly.");

        Gravity gravity = new(0, string.Empty);
        List<Task> tasks = new(models.Count);

        foreach (T model in models)
        {
            if (string.IsNullOrWhiteSpace(model.id))
                model.id = Guid.NewGuid().ToString();
            model.AddedOn = DateTime.UtcNow;

            tasks.Add(_container.CreateItemAsync(model, new PartitionKey(model.PartitionKey))
                .ContinueWith(response => gravity = new(gravity.RU + response.Result.RequestCharge, string.Empty)));
        }

        await Task.WhenAll(tasks);
        return gravity;
    }

    async Task<(Gravity, T)> IGalaxy<T>.Modify(T model)
    {
        try
        {
            model.ModifiedOn = DateTime.UtcNow;

            ItemResponse<T> response = await _container.ReplaceItemAsync(model, model.id, new PartitionKey(model.PartitionKey));
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

    async Task<Gravity> IGalaxy<T>.Modify(IList<T> models)
    {
        try
        {
            if (!_allowBulk)
                throw new UniverseException("Bulk modify of documents is not configured properly.");

            Gravity gravity = new(0, string.Empty);
            List<Task> tasks = new(models.Count);

            foreach (T model in models)
            {
                model.ModifiedOn = DateTime.UtcNow;

                tasks.Add(_container.ReplaceItemAsync(model, model.id, new PartitionKey(model.PartitionKey))
                    .ContinueWith(response =>
                    {
                        if (!response.IsCompletedSuccessfully)
                            throw new UniverseException(response.Exception?.Flatten().InnerException?.Message ?? "Oops! Something went wrong!");

                        gravity = new(gravity.RU + response.Result.RequestCharge, string.Empty);
                    }));
            }

            await Task.WhenAll(tasks);
            return gravity;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UniverseException($"Something went wrong doing the bulk operation. See error: {ex.Message}");
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
            ItemResponse<T> response = await _container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey));
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
            ItemResponse<T> response = await _container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
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
        using FeedIterator<T> queryResponse = _container.GetItemQueryIterator<T>(query);
        if (queryResponse.HasMoreResults)
        {
            FeedResponse<T> next = await queryResponse.ReadNextAsync();
            return (new(next.RequestCharge, null, _recordQuery ? (query.QueryText, query.GetQueryParameters()) : default), next.Any() ? next.Resource.FirstOrDefault() : default);
        }
        else return new(new(0, null), default);
    }

    async Task<(Gravity, T)> IGalaxy<T>.Get(IList<Cluster> clusters, IList<string> columns)
    {
        try
        {
            QueryDefinition query = CreateQuery(clusters, columnOptions: columns is null || !columns.Any() ? null : new(columns));
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
        using FeedIterator<T> queryResponse = _container.GetItemQueryIterator<T>(query);
        while (queryResponse.HasMoreResults)
        {
            FeedResponse<T> next = await queryResponse.ReadNextAsync();
            collection.AddRange(next);
            requestCharge += next.RequestCharge;
        }

        return (new(requestCharge, null, _recordQuery ? (query.QueryText, query.GetQueryParameters()) : default), collection);
    }

    async Task<(Gravity, IList<T>)> IGalaxy<T>.List(IList<Cluster> clusters, ColumnOptions? columnOptions, IList<Sorting.Option> sorting, IList<string> group)
    {
        try
        {
            QueryDefinition query = CreateQuery(clusters: clusters, columnOptions: columnOptions, sorting: sorting, groups: group);
            return await GetListFromQuery(query);
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    async Task<(Gravity, IList<T>)> IGalaxy<T>.Paged(Q.Page page, IList<Cluster> clusters, ColumnOptions? columnOptions, IList<Sorting.Option> sorting, IList<string> group)
    {
        try
        {
            QueryDefinition query = CreateQuery(clusters: clusters, columnOptions: columnOptions, sorting: sorting, groups: group);

            double requestUnit = 0;
            string continuationToken = string.Empty;
            List<T> collection = new();
            using FeedIterator<T> queryResponse = _container.GetItemQueryIterator<T>(query,
                requestOptions: new() { MaxItemCount = page.Size },
                continuationToken: string.IsNullOrWhiteSpace(page.ContinuationToken) ? null : page.ContinuationToken
            );
            while (queryResponse.HasMoreResults)
            {
                FeedResponse<T> next = await queryResponse.ReadNextAsync();
                collection.AddRange(next);
                requestUnit += next.RequestCharge;
                if (next.Count > 0 && !query.QueryText.Contains("GROUP BY"))
                {
                    continuationToken = next.ContinuationToken;
                    break;
                }
            }

            return (new(requestUnit, continuationToken, _recordQuery ? (query.QueryText, query.GetQueryParameters()) : default), collection);
        }
        catch (CosmosException ex) when (ex.StatusCode != HttpStatusCode.NotFound)
        {
            throw;
        }
    }

    /// <summary></summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue) return;
        if (disposing)
        {
        }

        _disposedValue = true;
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