using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using TalkPro.Core.Llm;

namespace TalkPro.Llm.OnDevice;

/// <summary>
/// Downloads and verifies an <see cref="OnDeviceModel"/> into a local folder (DD-014 allowed network use, DD-026).
/// <list type="bullet">
/// <item>HTTPS only, and every hop — including redirects to the CDN — must be on <see cref="AllowedHosts"/>.</item>
/// <item>Requests are plain GETs with at most a Range header: no body, no cookies, no credentials, nothing from the user.</item>
/// <item>Interrupted files resume; each finished file must match its pinned size and SHA-256 or it is deleted.</item>
/// <item>A marker file records the verified hashes, so start-up does not re-hash gigabytes.</item>
/// </list>
/// </summary>
public sealed class ModelDownloader : IModelInstaller, IDisposable
{
    public const string MarkerFileName = "talkpro-verified.json";
    private const int MaxRedirects = 5;
    private const string PartSuffix = ".part";

    private readonly OnDeviceModel _model;
    private readonly string _directory;
    private readonly HttpClient _http;

    public ModelDownloader(OnDeviceModel model, string directory)
        : this(model, directory, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        })
    {
    }

    internal ModelDownloader(OnDeviceModel model, string directory, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _model = model;
        _directory = directory;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.Clear();
    }

    /// <summary>Hugging Face and its download CDNs. Anything else is refused, also as a redirect target.</summary>
    public static IReadOnlyList<string> AllowedHosts { get; } = ["huggingface.co", ".huggingface.co", ".hf.co"];

    public ModelSpec Model => _model.Spec;

    public long TotalBytes => _model.TotalBytes;

    public string Directory => _directory;

    public bool IsInstalled => IsVerified(_model, _directory);

    public static bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        return AllowedHosts.Any(a => a.StartsWith('.') ? host.EndsWith(a, StringComparison.Ordinal) : host == a);
    }

    /// <summary>True when every file is present with its pinned size and the marker lists the pinned hashes.</summary>
    public static bool IsVerified(OnDeviceModel model, string directory)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            var marker = Path.Combine(directory, MarkerFileName);
            if (!File.Exists(marker))
            {
                return false;
            }

            var recorded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(marker)) ?? [];
            return model.Files.All(f =>
                recorded.TryGetValue(f.Name, out var sha) && sha == f.Sha256
                && new FileInfo(Path.Combine(directory, f.Name)) is { Exists: true } info && info.Length == f.Size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public async Task<ModelInstallError> InstallAsync(IProgress<ModelInstallProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            File.Delete(Path.Combine(_directory, MarkerFileName));
            long done = 0;
            foreach (var file in _model.Files)
            {
                var error = await DownloadFileAsync(file, done, progress, cancellationToken).ConfigureAwait(false);
                if (error != ModelInstallError.None)
                {
                    return error;
                }

                done += file.Size;
            }

            await File.WriteAllTextAsync(
                Path.Combine(_directory, MarkerFileName),
                JsonSerializer.Serialize(_model.Files.ToDictionary(f => f.Name, f => f.Sha256)),
                cancellationToken).ConfigureAwait(false);
            return ModelInstallError.None;
        }
        catch (OperationCanceledException)
        {
            return ModelInstallError.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModelInstallError.Storage;
        }
    }

    public void Remove()
    {
        if (System.IO.Directory.Exists(_directory))
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
    }

    public void Dispose() => _http.Dispose();

    public override string ToString() => $"ModelDownloader {{ {_model.Spec} }}";

    private async Task<ModelInstallError> DownloadFileAsync(ModelFile file, long before, IProgress<ModelInstallProgress>? progress, CancellationToken cancellationToken)
    {
        var target = Path.Combine(_directory, file.Name);
        var part = target + PartSuffix;
        if (File.Exists(target) && new FileInfo(target).Length == file.Size && await HashAsync(target, cancellationToken).ConfigureAwait(false) == file.Sha256)
        {
            progress?.Report(new ModelInstallProgress(before + file.Size, TotalBytes));
            return ModelInstallError.None;
        }

        File.Delete(target);
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > file.Size)
        {
            File.Delete(part);
            existing = 0;
        }

        if (existing < file.Size)
        {
            using var response = await GetAsync(new Uri(_model.BaseUri, file.Name), existing, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                return ModelInstallError.Network;
            }

            var resumed = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
            if (!resumed)
            {
                existing = 0;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                await using (output.ConfigureAwait(false))
                {
                    var buffer = new byte[1 << 16];
                    var written = existing;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        if (written + read > file.Size)
                        {
                            return Fail(part, ModelInstallError.HashMismatch);
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        written += read;
                        progress?.Report(new ModelInstallProgress(before + written, TotalBytes));
                    }
                }
            }
        }

        if (new FileInfo(part).Length != file.Size || await HashAsync(part, cancellationToken).ConfigureAwait(false) != file.Sha256)
        {
            return Fail(part, ModelInstallError.HashMismatch);
        }

        File.Move(part, target, overwrite: true);
        return ModelInstallError.None;
    }

    /// <summary>GET with manual redirects, each hop checked against the allow-list. Null on any refusal or failure.</summary>
    private async Task<HttpResponseMessage?> GetAsync(Uri uri, long offset, CancellationToken cancellationToken)
    {
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowed(uri))
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                continue;
            }

            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent)
            {
                return response;
            }

            response.Dispose();
            return null;
        }

        return null;
    }

    private static ModelInstallError Fail(string part, ModelInstallError error)
    {
        File.Delete(part);
        return error;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
    }
}
