using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MimeMapping;

namespace Photobook.Services;

public class AzureStorageService
{
    private readonly BlobServiceClient blobServiceClient;
    private readonly string containerName;

    public AzureStorageService(IConfiguration configuration)
    {
        blobServiceClient = new BlobServiceClient(configuration.GetConnectionString("AzureStorageConnection"));
        containerName = configuration.GetValue<string>("AppSettings:ContainerName").ToLowerInvariant();
    }

    public async Task SaveAsync(string path, Stream stream, bool overwrite = false)
    {
        var blobClient = await GetBlobClientAsync(path, true).ConfigureAwait(false);

        if (!overwrite)
        {
            var blobExists = await blobClient.ExistsAsync().ConfigureAwait(false);
            if (blobExists)
            {
                throw new IOException($"The file {path} already exists.");
            }
        }

        stream.Position = 0;
        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = MimeUtility.GetMimeMapping(path) }).ConfigureAwait(false);
    }

    public async Task<Stream?> ReadAsync(string path)
    {
        var blobClient = await GetBlobClientAsync(path).ConfigureAwait(false);

        var blobExists = await blobClient.ExistsAsync().ConfigureAwait(false);
        if (!blobExists)
        {
            return null;
        }

        var stream = await blobClient.OpenReadAsync();
        return stream;
    }

    public async Task DeleteAsync(string path)
    {
        var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
        await blobContainerClient.DeleteBlobIfExistsAsync(path).ConfigureAwait(false);
    }

    private async Task<BlobClient> GetBlobClientAsync(string blobName, bool createIfNotExists = false)
    {
        var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
        if (createIfNotExists)
        {
            await blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.None).ConfigureAwait(false);
        }

        var blobClient = blobContainerClient.GetBlobClient(blobName.ToLowerInvariant());
        return blobClient;
    }
}
