namespace Universe.Options.Query;

/// <summary></summary>
public struct Sorting
{
    /// <summary></summary>
    public enum Direction
    {
        /// <summary>Ascending</summary>
        ASC,

        /// <summary>Descending</summary>
        DESC
    }

    /// <summary></summary>
    public record Option(string Column, Direction Direction = Direction.ASC);
}

/// <summary></summary>
public static class SortOptionDirectionExtension
{
    /// <summary></summary>
    public static string Value(this Sorting.Direction direction) => direction switch
    {
        Sorting.Direction.ASC => "ASC",
        Sorting.Direction.DESC => "DESC",
        _ => throw new UniverseException("Unrecognized SORT DIRECTION keyword")
    };
}