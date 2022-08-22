namespace Universe.Response;

/// <summary>Query execution response</summary>
/// <param name="RU">RU consumed</param>
/// <param name="ContinuationToken">Pagination Token</param>
/// <param name="Query">Query that was executed</param>
public record Gravity(double RU, string ContinuationToken, (string Text, IReadOnlyList<(string, object)> Parameters) Query = default);