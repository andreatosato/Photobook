namespace Photobook;

public static class FormFileExtensions
{
    private static readonly IEnumerable<string> contentTypes = new[]
    {
        "image/jpg", "image/jpeg","image/pjpeg","image/gif", "image/x-png","image/png"
    };

    private static readonly IEnumerable<string> extensions = new[]
    {
        ".jpg", ".png",".gif",".jpeg"
    };

    public static bool IsImage(this IFormFile postedFile)
    {
        var contentType = postedFile.ContentType.ToLowerInvariant();
        var extension = Path.GetExtension(postedFile.FileName).ToLowerInvariant();

        return contentTypes.Contains(contentType) && extensions.Contains(extension);
    }
}
