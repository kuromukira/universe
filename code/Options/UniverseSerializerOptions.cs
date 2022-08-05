using System.IO;
using Azure.Core.Serialization;

namespace Universe.Options;

/// <summary></summary>
public class UniverseSerializer : CosmosSerializer
{
    private readonly JsonObjectSerializer SystemTextJsonSerializer;

    /// <summary></summary>
    public UniverseSerializer() => SystemTextJsonSerializer = new(new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });

    /// <summary></summary>
    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (stream.CanSeek && stream.Length == 0)
                return default;

            if (typeof(Stream).IsAssignableFrom(typeof(T)))
                return (T)(object)stream;

            return (T)SystemTextJsonSerializer.Deserialize(stream, typeof(T), default);
        }
    }

    /// <summary></summary>
    public override Stream ToStream<T>(T input)
    {
        MemoryStream streamPayload = new();
        SystemTextJsonSerializer.Serialize(streamPayload, input, typeof(T), default);
        streamPayload.Position = 0;
        return streamPayload;
    }
}
