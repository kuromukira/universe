namespace Universe.Options.Query;

public struct Sorting
{
    public enum Direction
    {
        ASC,
        DESC
    }

    public record Option(string Column, Direction Direction = Direction.ASC);
}

public static class SortOptionDirectionExtension
{
    public static string Value(this Sorting.Direction direction) => direction switch
    {
        Sorting.Direction.ASC => "ASC",
        Sorting.Direction.DESC => "DESC",
        _ => throw new UniverseException("Unrecognized SORT DIRECTION keyword")
    };
}