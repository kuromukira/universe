namespace SimpleCosmos.Options.Query;

public static class Query
{
    public struct Where
    {
        public const string And = "AND";
        public const string Or = "OR";
    }

    public struct Operator
    {
        public const string Eq = "=";
        public const string NotEq = "!=";
        public const string Gt = ">";
        public const string Gte = ">=";
        public const string Lt = "<";
        public const string Lte = "<=";
        public const string In = "IN";
        public const string Notin = "NOT IN";
        public const string Like = "LIKE";
        public const string NotLike = "NOT LIKE";
        public const string IsNull = "IS NULL";
        public const string IsNotNull = "IS NOT NULL";
    }
}
