namespace Photobook.Logging;

internal partial class ProgramLogger
{
    private readonly ILogger<Program> logger;

    public ProgramLogger(ILogger<Program> logger)
    {
        this.logger = logger;
    }

    [LoggerMessage(42, LogLevel.Information, "Request MapGet Photos")]
    public partial void LogRequestMapGetPhotos();

    [LoggerMessage(100, LogLevel.Information, "Response MapGet Number of photos: {photos}")]
    public partial void LogResponseMapGetPhotos(int photos);
}