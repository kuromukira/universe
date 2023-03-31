namespace Universe.Options.Query;

/// <summary>
/// Clusters are group of Catalysts that are joined by a Where operator (eg AND / OR). This will divide the where clause into multiple groups.
/// </summary>
/// <param name="Catalysts">Catalysts under one group / cluster</param>
/// <param name="Where">Where operator (eg AND / OR)</param>
public readonly record struct Cluster(IList<Catalyst> Catalysts, Q.Where Where = Q.Where.And);