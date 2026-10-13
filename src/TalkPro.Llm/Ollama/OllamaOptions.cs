using System.Net;

namespace TalkPro.Llm.Ollama;

/// <summary>
/// Settings of the local Ollama runtime. Fixed in code (no environment variables or config files, DD-005);
/// the endpoint must be a loopback IP literal so conversation data can never leave the PC (DD-014/DD-023).
/// </summary>
public sealed record OllamaOptions
{
    public static readonly Uri DefaultEndpoint = new("http://127.0.0.1:11434/");

    private readonly Uri _endpoint = DefaultEndpoint;

    public Uri Endpoint
    {
        get => _endpoint;
        init => _endpoint = LoopbackEndpoint.Validate(value);
    }

    public TimeSpan GenerationTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan StatusTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public int MaxResponseBytes { get; init; } = 4 * 1024 * 1024;

    public int MaxEmbeddingBatch { get; init; } = 128;

    public override string ToString() => $"OllamaOptions {{ Endpoint = {Endpoint} }}";
}

public static class LoopbackEndpoint
{
    /// <summary>
    /// Accepts only <c>http://127.0.0.1:port/</c> or <c>http://[::1]:port/</c>. Host names (including
    /// "localhost", which a hosts file can redirect), other addresses, credentials, paths and queries are rejected.
    /// </summary>
    public static Uri Validate(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri
            || endpoint.Scheme != Uri.UriSchemeHttp
            || endpoint.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6)
            || !IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out var address)
            || !IPAddress.IsLoopback(address)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || endpoint.AbsolutePath != "/"
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("The local AI endpoint must be http://127.0.0.1:port/ or http://[::1]:port/.", nameof(endpoint));
        }

        return endpoint;
    }

    public static bool IsLoopbackLiteral(string host) =>
        IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
}
