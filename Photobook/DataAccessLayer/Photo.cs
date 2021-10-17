namespace Photobook.DataAccessLayer;

public class Photo
{
    public Guid Id { get; set; }

    public string OriginalFileName { get; set; } = null!;

    public string Path { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime UploadDate { get; set; }
}
