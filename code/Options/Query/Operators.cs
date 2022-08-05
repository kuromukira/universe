﻿namespace Universe.Options.Query;

/// <summary></summary>
public static class Query
{
    /// <summary>Page defintion for paginated queries</summary>
    public record Page(int Size, string ContinuationToken);

    /// <summary>AND / OR where clause operators</summary>
    public struct Where
    {
        /// <summary></summary>
        public const string And = "AND";

        /// <summary></summary>
        public const string Or = "OR";
    }

    /// <summary>Equality operator</summary>
    public struct Operator
    {
        /// <summary></summary>
        public const string Eq = "=";

        /// <summary></summary>
        public const string NotEq = "!=";

        /// <summary></summary>
        public const string Gt = ">";

        /// <summary></summary>
        public const string Gte = ">=";

        /// <summary></summary>
        public const string Lt = "<";

        /// <summary></summary>
        public const string Lte = "<=";

        /// <summary></summary>
        public const string In = "IN";

        /// <summary></summary>
        public const string Notin = "NOT IN";

        /// <summary></summary>
        public const string Like = "LIKE";

        /// <summary></summary>
        public const string NotLike = "NOT LIKE";
    }
}
