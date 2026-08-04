using Universe.Response;

namespace Universe.Interfaces;

/// <summary></summary>
public interface IGalaxyBasic<T> where T : ICosmicEntity
{
    /// <summary>
    /// Starts an atomic transactional batch for one logical partition.
    /// </summary>
    /// <param name="partitionKeys">Exact order of the repository partition-key values.</param>
    /// <remarks>The default implementation throws <see cref="NotSupportedException"/> for compatibility with implementations that predate batch operations.</remarks>
    AtomicBatch<T> Atomic(params string[] partitionKeys)
        => throw new NotSupportedException("This IGalaxyBasic implementation does not support atomic batch operations.");

    /// <summary>
    /// Starts a cross-partition bulk batch. Operations in one partition are transactional; operations across partitions are not.
    /// </summary>
    /// <remarks>The default implementation throws <see cref="NotSupportedException"/> for compatibility with implementations that predate batch operations.</remarks>
    BulkBatch<T> Bulk()
        => throw new NotSupportedException("This IGalaxyBasic implementation does not support bulk batch operations.");

    /// <summary>
    /// Create a new model in the database
    /// </summary>
    Task<(Gravity g, string t)> Create(T model);

    /// <summary>
    /// Bulk create new models in the database
    /// </summary>
    Task<Gravity> Create(IReadOnlyList<T> models);

    /// <summary>
    /// Modify a model in the database
    /// </summary>
    Task<(Gravity g, T T)> Modify(T model);

    /// <summary>
    /// Bulk modify models in the database
    /// </summary>
    Task<Gravity> Modify(IReadOnlyList<T> models);

    /// <summary>
    /// Remove one model from the database
    /// </summary>
    Task<Gravity> Remove(string id, string partitionKey);

    /// <summary>
    /// Remove one model from the database
    /// </summary>
    /// <param name="id"></param>
    /// <param name="partitionKey">Exact order of your defined partition keys</param>
    Task<Gravity> Remove(string id, params string[] partitionKey);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    Task<(Gravity g, T T)> Get(string id, string partitionKey);

    /// <summary>
    /// Get one model from the database
    /// </summary>
    /// <param name="id"></param>
    /// <param name="partitionKey">Exact order of your defined partition keys</param>
    Task<(Gravity g, T T)> Get(string id, params string[] partitionKey);
}
