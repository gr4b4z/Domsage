using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;

namespace AgentPlatform.Infrastructure.Blob;

/// <summary>Local-disk blob storage for MVP. Singleton.</summary>
public sealed class LocalBlobStorage : IBlobStorage
{
    private readonly string _root;

    public LocalBlobStorage(IConfiguration config)
    {
        _root = config["BlobStorage:LocalPath"]
            ?? Path.Combine(Path.GetTempPath(), "agentplatform-blobs");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> StoreAsync(Stream data, string mediaType, CancellationToken ct)
    {
        var ext = mediaType.Split('/').LastOrDefault() ?? "bin";
        var name = $"{Guid.NewGuid():N}.{ext}";
        var path = Path.Combine(_root, name);
        await using var fs = File.Create(path);
        await data.CopyToAsync(fs, ct);
        return name;
    }

    public Task<Stream> ReadAsync(string storageRef, CancellationToken ct)
    {
        var path = Path.Combine(_root, storageRef);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }
}
