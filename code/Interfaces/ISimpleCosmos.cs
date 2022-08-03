namespace SimpleCosmos.Interfaces;

/// <summary></summary>
public interface ISimpleCosmos<T> where T : ICosmosEntity
{
    /// <summary>
    /// Create a new model in the database
    /// </summary>
    Task<string> Create(T model);

    /// <summary>
    /// Create a new model in the database
    /// </summary>
    Task Create(IList<QueryParameter> parameters, T model);

    /// <summary>
    /// Modify a model in the database
    /// </summary>
    Task<T> Modify(T model);

    /// <summary>
    /// Remove one model from the database
    /// </summary>
    Task Remove(string id, string partitionKey);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<T> Get(QueryParameter parameter);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<T> Get(string id, string partitionKey);

    /// <summary>
    /// Get a paginated list from the database
    /// </summary>
    Task<IList<T>> Get(IList<QueryParameter> parameters);
}
