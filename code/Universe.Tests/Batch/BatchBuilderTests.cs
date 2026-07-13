using System.Text.Json;
using System.Text.Json.Serialization;
using Universe;
using Universe.Attributes;
using Universe.Exception;
using Universe.Interfaces;
using Xunit;

namespace Universe.Tests.Batch;

public sealed class BatchBuilderTests
{
    [Fact]
    public void PatchBuilder_UsesJsonPropertyNameAndNamingPolicyForNestedPaths()
    {
        PatchBuilder<BatchEntity> builder = new(JsonNamingPolicy.CamelCase);

        builder
            .Set(entity => entity.DisplayName, "updated")
            .Add(entity => entity.Metadata.Score, 5)
            .Replace(entity => entity.Metadata.Score, 6)
            .Increment(entity => entity.Metadata.Score, 1)
            .Remove(entity => entity.Metadata.Legacy);

        Assert.Equal(
            ["/display_name", "/metadata/score", "/metadata/score", "/metadata/score", "/metadata/legacy"],
            builder.Operations.Select(operation => operation.Path));
        Assert.Equal(5, builder.Operations.Count);
    }

    [Fact]
    public void PatchConditionBuilder_UsesSafeBracketedPathsAndExplicitConnectors()
    {
        PatchConditionBuilder<BatchEntity> builder = new(JsonNamingPolicy.CamelCase);

        builder
            .Equal(entity => entity.DisplayName, "queued")
            .And()
            .GreaterThanOrEqual(entity => entity.Metadata.Score, 3);

        Assert.Equal("FROM c WHERE c[\"display_name\"] = \"queued\" AND c[\"metadata\"][\"score\"] >= 3", builder.Build());
    }

    [Fact]
    public void PatchConditionBuilder_RejectsComplexValuesAndImplicitChaining()
    {
        PatchConditionBuilder<BatchEntity> complex = new(JsonNamingPolicy.CamelCase);
        Assert.Throws<UniverseException>(() => complex.Equal(entity => entity.Metadata, new BatchMetadata()));

        PatchConditionBuilder<BatchEntity> chained = new(JsonNamingPolicy.CamelCase);
        chained.Equal(entity => entity.DisplayName, "one");
        Assert.Throws<UniverseException>(() => chained.Equal(entity => entity.DisplayName, "two"));
    }

    [Fact]
    public void PatchBuilder_RejectsUnsupportedSelectorAndEleventhOperation()
    {
        PatchBuilder<BatchEntity> selector = new(JsonNamingPolicy.CamelCase);
        Assert.Throws<UniverseException>(() => selector.Set(entity => entity.DisplayName.ToLower(), "x"));

        PatchBuilder<BatchEntity> limit = new(JsonNamingPolicy.CamelCase);
        for (int index = 0; index < 10; index++)
            limit.Set(entity => entity.DisplayName, index.ToString());

        Assert.Throws<UniverseException>(() => limit.Set(entity => entity.DisplayName, "eleven"));
    }

    private sealed record BatchEntity : CosmicEntity
    {
        [PartitionKey]
        public string TenantId { get; set; }

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; }

        public BatchMetadata Metadata { get; set; } = new();
    }

    private sealed record BatchMetadata
    {
        public int Score { get; set; }
        public string Legacy { get; set; }
    }
}
