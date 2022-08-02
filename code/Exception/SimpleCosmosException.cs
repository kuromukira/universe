using System.Runtime.Serialization;

namespace SimpleCosmos.Exception;

[Serializable]
public sealed class SimpleCosmosException : System.Exception
{
    private const string HelpUrl = "https://github.com/kuromukira/simple-cosmos/issues";

    /// <summary>Custom exception</summary>
    public SimpleCosmosException()
        => HelpLink = HelpUrl;

    /// <summary>Custom exception</summary>
    public SimpleCosmosException(string message) : base(message)
        => HelpLink = HelpUrl;

    /// <summary>Custom exception</summary>
    public SimpleCosmosException(string message, System.Exception innerException) : base(message, innerException)
        => HelpLink = HelpUrl;

    /// <summary>Custom exception</summary>
    public SimpleCosmosException(SerializationInfo info, StreamingContext context) : base(info, context)
        => HelpLink = HelpUrl;
}
