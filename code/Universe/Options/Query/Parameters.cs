namespace Universe.Options.Query;

/// <summary>
/// Create a generic query string from a list of parameters. The first parameter created will be the first parameter in the where clause of the query string.
/// </summary>
/// <param name="Column">Column name. Excluding the @ symbol for SQL parameters</param>
/// <param name="Value">Value for the where clause associated with the Column</param>
/// <param name="Where">Where operator (eg AND / OR)</param>
/// <param name="Operator">Boolean expression operator</param>
public record struct Catalyst(
    string Column,
    object Value,
    Q.Where Where = Q.Where.And,
    Q.Operator Operator = Q.Operator.Eq);
