using System.Text.RegularExpressions;

namespace Universe.Options.Query;

/// <summary>
/// Create a generic query string from a list of parameters. The first parameter created will be the first parameter in the where clause of the query string.
/// </summary>
/// <param name="Column">Column name</param>
/// <param name="Value">Value for the where clause associated with the Column</param>
/// <param name="Alias">
///     Column name alias excluding the @ symbol for SQL Parameters.
///     If not supplied, this will use the value from <paramref name="Column"/>
/// </param>
/// <param name="Where">Where operator (eg AND / OR)</param>
/// <param name="Operator">Boolean expression operator</param>
public record struct Catalyst(
    string Column,
    object Value,
    string Alias = null,
    Q.Where Where = Q.Where.And,
    Q.Operator Operator = Q.Operator.Eq);

/// <summary></summary>
public static class CatalystExtension
{
    /// <summary></summary>
    public static string ParameterName(this Catalyst catalyst)
    {
        if (string.IsNullOrWhiteSpace(catalyst.Alias))
            return Regex.Replace(catalyst.Column, "[^\\w\\d]", string.Empty);

        return catalyst.Alias;
    }
}
