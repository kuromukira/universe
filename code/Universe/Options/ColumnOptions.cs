namespace Universe.Options;

/// <summary></summary>
/// <param name="Names">List of column names to be part of the query</param>
/// <param name="IsDistinct">Adds DISTINCT in the generated query</param>
/// <param name="Top">Only select the specified number of top rows</param>
/// <param name="Count">
///     Count the number of rows. Please note that the column <paramref name="Names"/>
///     will be added as part of the GROUP BY clause
/// </param>
public record struct Column(
    IList<string> Names,
    bool IsDistinct = false,
    int Top = 0,
    bool Count = false);