using System.Text.RegularExpressions;

namespace Universe.Options.Query;

/// <summary>
/// Create a generic query string from a list of parameters. The first parameter created will be the first parameter in the where clause of the query string.
/// </summary>
/// <param name="Column">Column name</param>
/// <param name="Value">Value for the where clause associated with the Column</param>
/// <param name="Where">Where operator (eg AND / OR)</param>
/// <param name="Operator">Boolean expression operator</param>
public readonly record struct Catalyst(
    string Column,
    object Value = null,
    Q.Where Where = Q.Where.And,
    Q.Operator Operator = Q.Operator.Eq)
{
    /// <summary>Creates a list of rule violations when creating a CosmosDb query catalyst</summary>
    public IEnumerable<string> RuleViolations()
    {
        List<string> violations = new();

        // Column name cannot be null or empty
        if (string.IsNullOrWhiteSpace(Column))
            violations.Add("Column name is required");

        // Value should be null if using IS_DEFINED or NOT IS_DEFINED operators
        if (Value is not null && Operator is Q.Operator.Defined or Q.Operator.NotDefined)
            violations.Add("Value should not be supplied when using the Defined or NotDefined operators");

        // Value should have wildcard for Like and Not Like operators
        if (Value is not null && Operator is Q.Operator.Like or Q.Operator.NotLike)
        {
            string value = Value.ToString();
            if (string.IsNullOrWhiteSpace(value))
                violations.Add("Value is required when using the Like or NotLike operators");
            else if (!value.Contains('%'))
                violations.Add("Value should contain a wildcard (%) for Like and NotLike operators");
        }

        // Value is required for all other operators
        if (Value is null && Operator is not Q.Operator.Defined and not Q.Operator.NotDefined)
            violations.Add("Value is required for all operators except Defined and NotDefined");

        return violations;
    }
}

/// <summary></summary>
public static class CatalystExtension
{
    /// <summary></summary>
    public static string ParameterName(this Catalyst catalyst) => Regex.Replace(catalyst.Column, "[^\\w\\d]", string.Empty);
}