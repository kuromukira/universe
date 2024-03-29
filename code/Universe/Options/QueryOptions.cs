﻿namespace Universe.Options.Query;

/// <summary>Query Options</summary>
public struct Q
{
    /// <summary>Page definition for paginated queries</summary>
    public record Page(int Size, string ContinuationToken = null);

    /// <summary>AND / OR where clause operators</summary>
    public enum Where
    {
        /// <summary></summary>
        And,

        /// <summary></summary>
        Or
    }

    /// <summary>Equality operator</summary>
    public enum Operator
    {
        /// <summary>Equal</summary>
        Eq,

        /// <summary>Not Equal</summary>
        NotEq,

        /// <summary>Greater Than</summary>
        Gt,

        /// <summary>Greater Than Or Equal</summary>
        Gte,

        /// <summary>Lower Than</summary>
        Lt,

        /// <summary>Lower Than Or Equal</summary>
        Lte,

        /// <summary>In</summary>
        In,

        /// <summary>Not In</summary>
        NotIn,

        /// <summary>Like</summary>
        Like,

        /// <summary>Not Like</summary>
        NotLike,
        
        /// <summary>IS_DEFINED</summary>
        Defined,
        
        /// <summary>NOT IS_DEFINED</summary>
        NotDefined
    }
}

/// <summary></summary>
public static class WhereExtension
{
    /// <summary></summary>
    public static string Value(this Q.Where where) => where switch
    {
        Q.Where.And => "AND",
        Q.Where.Or => "OR",
        _ => throw new UniverseException("Unrecognized WHERE keyword")
    };
}

/// <summary></summary>
public static class OperatorExtension
{
    /// <summary></summary>
    public static string Value(this Q.Operator opr) => opr switch
    {
        Q.Operator.Eq => "=",
        Q.Operator.NotEq => "!=",
        Q.Operator.Gt => ">",
        Q.Operator.Gte => ">=",
        Q.Operator.Lt => "<",
        Q.Operator.Lte => "<=",
        Q.Operator.In => "IN",
        Q.Operator.NotIn => "NOT IN",
        Q.Operator.Like => "LIKE",
        Q.Operator.NotLike => "NOT LIKE",
        Q.Operator.Defined => "IS_DEFINED",
        Q.Operator.NotDefined => "NOT IS_DEFINED",
        _ => throw new UniverseException("Unrecognized OPERATOR keyword")
    };
}
