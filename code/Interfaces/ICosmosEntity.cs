namespace SimpleCosmos.Interfaces;

/// <summary>Base interface for Cosmos Entities</summary>
public interface ICosmosEntity
{
    /// <summary>Unique GUID</summary>
#pragma warning disable IDE1006 // Naming Styles
    public string id { get; set; }
#pragma warning restore IDE1006 // Naming Styles

    /// <summary>UTC Date document was added</summary>
    public DateTime AddedOn { get; set; }

    /// <summary>UTC Date document was modified.</summary>
    public DateTime? ModifiedOn { get; set; }

    /// <summary>Set the value for the PartitionKey field</summary>
    public abstract string PartitionKey { get; }
}
