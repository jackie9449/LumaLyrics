using System.Net;
using System.Security.Cryptography;

namespace LumaLyrics.Infrastructure;

/// <summary>Installs only fixed, verified artifacts outside the software directory.</summary>
public sealed class InstrumentModelStore
{
    public const long MaximumModelBytes = 512L * 1024 * 1024;
    static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };
    readonly string root;
    readonly HttpClient http;
    readonly SemaphoreSlim installGate = new(1);

    public InstrumentModelStore(string dataRoot, HttpClient? httpClient = null)
    {
        root = InstrumentStoragePaths.CheckRoot(dataRoot);
        http = httpClient ?? SharedHttp;
    }

    public async Task<bool> ValidateAsync(string modelPath, string expectedSha256, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!InstrumentModelManifest.IsHex(expectedSha256, 64)) return false;
        try
        {
            await using var file = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            if (file.Length is <= 0 or > MaximumModelBytes) return false;
            byte[] actual = await SHA256.HashDataAsync(file, token);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedSha256));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    public async Task<string> EnsureAsync(InstrumentModelManifest manifest, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        manifest.Validate();
        if (!AllowedSource(manifest.DownloadUri)) throw new InvalidDataException("Model download origin is not approved.");
        await installGate.WaitAsync(token);
        string? partial = null;
        try
        {
            string directory = Path.Combine(root, "models", "instruments", manifest.Id, manifest.Sha256.ToLowerInvariant());
            RejectLinks(directory);
            Directory.CreateDirectory(directory);
            RejectLinks(directory);
            string destination = Path.Combine(directory, "model.onnx");
            if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Model destination cannot be a link.");
            if (await ValidateAsync(destination, manifest.Sha256, token) && new FileInfo(destination).Length == manifest.Bytes)
                return destination;
            using var response = await DownloadAsync(manifest.DownloadUri, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } size && size != manifest.Bytes)
                throw new InvalidDataException("Unexpected model download size.");
            partial = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    copied = checked(copied + read);
                    if (copied > manifest.Bytes) throw new InvalidDataException("Model download exceeds the expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (copied != manifest.Bytes) throw new InvalidDataException("Model download was truncated.");
                await output.FlushAsync(token);
            }
            if (!await ValidateAsync(partial, manifest.Sha256, token)) throw new InvalidDataException("Model checksum failed.");
            token.ThrowIfCancellationRequested();
            RejectLinks(directory);
            if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Model destination cannot be a link.");
            File.Move(partial, destination, true);
            partial = null;
            return destination;
        }
        finally
        {
            try { if (partial is not null && File.Exists(partial)) File.Delete(partial); }
            finally { installGate.Release(); }
        }
    }

    async Task<HttpResponseMessage> DownloadAsync(Uri uri, CancellationToken token)
    {
        for (int hop = 0; hop < 4; hop++)
        {
            if (!AllowedSource(uri)) throw new InvalidDataException("Model redirect origin is not approved.");
            var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.RequestMessage?.RequestUri is { } actual && !AllowedSource(actual))
            {
                response.Dispose(); throw new InvalidDataException("Model request was redirected outside approved origins.");
            }
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("Model redirect has no destination.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            return response;
        }
        throw new InvalidDataException("Too many model redirects.");
    }

    static bool AllowedSource(Uri uri) => InstrumentModelManifest.IsHttps(uri) && (
        uri.Host == "storage.googleapis.com" && uri.AbsolutePath.StartsWith("/audioset/", StringComparison.Ordinal)
        || uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/jackie9449/LumaLyrics/releases/download/", StringComparison.Ordinal)
        || uri.Host == "release-assets.githubusercontent.com");

    static void RejectLinks(string directory)
    {
        for (var item = new DirectoryInfo(directory); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Model directories cannot contain links or redirections.");
    }
}
