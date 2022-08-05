using UniverseQuery.Responses;

namespace Universe.Interfaces;

/// <summary></summary>
public interface IGalaxy<T> where T : ICosmicEntity
{
    /// <summary>
    /// Create a new model in the database
    /// </summary>
    Task<(Gravity, string)> Create(T model);

    /// <summary>
    /// Create a new model in the database
    /// </summary>
    Task<Gravity> Create(IList<QueryParameter> parameters, T model);

    /// <summary>
    /// Modify a model in the database
    /// </summary>
    Task<(Gravity, T)> Modify(T model);

    /// <summary>
    /// Remove one model from the database
    /// </summary>
    Task<Gravity> Remove(string id, string partitionKey);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<(Gravity, T)> Get(string id, string partitionKey);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<(Gravity, T)> Get(QueryParameter parameter, IList<string> columns = null);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<(Gravity, T)> Get(IList<QueryParameter> parameters, IList<string> columns = null);

    /// <summary>
    /// Get a paginated list from the database
    /// </summary>
    Task<(Gravity, IList<T>)> List(QueryParameter parameter, IList<string> columns = null);

    /// <summary>
    /// Get a paginated list from the database
    /// </summary>
    Task<(Gravity, IList<T>)> List(IList<QueryParameter> parameters, IList<string> columns = null);

    /// <summary>
    /// Get a paginated list from the database
    /// </summary>
    Task<(Gravity, IList<T>)> Paged(Query.Page page, IList<QueryParameter> parameters, IList<string> columns = null);
}
