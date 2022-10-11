using Universe.Response;

namespace Universe.Interfaces;

/// <summary></summary>
public interface IGalaxy<T> where T : ICosmicEntity
{
    /// <summary>
    /// Create a new model in the database
    /// </summary>
    Task<(Gravity g, string t)> Create(T model);

    /// <summary>
    /// Create a new model in the database
    /// </summary>
    Task<Gravity> Create(IList<Catalyst> catalysts, T model);

    /// <summary>
    /// Modify a model in the database
    /// </summary>
    Task<(Gravity g, T T)> Modify(T model);

    /// <summary>
    /// Remove one model from the database
    /// </summary>
    Task<Gravity> Remove(string id, string partitionKey);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<(Gravity g, T T)> Get(string id, string partitionKey);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<(Gravity g, T T)> Get(Catalyst catalyst, IList<string> columns = null);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<(Gravity g, T T)> Get(IList<Catalyst> catalysts, IList<string> columns = null);

    /// <summary>
    /// Get a paginated list from the database
    /// </summary>
    Task<(Gravity g, IList<T> T)> List(Catalyst catalyst, IList<string> columns = null, IList<Sorting.Option> sorting = null, IList<string> group = null);

    /// <summary>
    /// Get a paginated list from the database
    /// </summary>
    Task<(Gravity g, IList<T> T)> List(IList<Catalyst> catalysts, IList<string> columns = null, IList<Sorting.Option> sorting = null, IList<string> group = null);

    /// <summary>
    /// Get a paginated list from the database
    /// </summary>
    Task<(Gravity g, IList<T> T)> Paged(Q.Page page, IList<Catalyst> catalysts, IList<string> columns = null, IList<Sorting.Option> sorting = null, IList<string> group = null);
}
