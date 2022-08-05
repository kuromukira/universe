using System.Data;

namespace Universe.Options.Query;

/// <summary>
/// Create a generic query string from a list of parameters. The first parameter created will be the first parameter in the where clause of the query string.
/// </summary>
/// <param name="Column">Column name. Excluding the @ symbol for SQL parameters</param>
/// <param name="Value">Value for the where clause associated with the Column</param>
/// <param name="Type">Data Type recognized by SQL Server</param>
/// <param name="Where">Where operator (eg AND / OR)</param>
/// <param name="Operator">Boolean expression operator</param>
public record struct QueryParameter(
    string Column,
    object Value,
    DbType Type,
    string Where = Query.Where.And,
    string Operator = Query.Operator.Eq);
