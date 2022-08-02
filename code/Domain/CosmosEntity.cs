namespace SimpleCosmos.Domain;

/// <summary>Base class for Cosmos Entities</summary>
public abstract class SimpleCosmosEntity
{
    /// <summary>Unique GUID</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>UTC Date document was added</summary>
    [JsonPropertyName("addedOn")]
    public DateTime AddedOn { get; set; }

    /// <summary>UTC Date document was modified</summary>
    [JsonPropertyName("modifiedOn")]
    public DateTime ModifiedOn { get; set; }

    [JsonIgnore] public abstract string PartitionKey { get; }
}
