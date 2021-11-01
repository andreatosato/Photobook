using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace Photobook.Services;

public class ComputerVisionService
{
    private readonly ComputerVisionClient computerVisionClient;
    private readonly ComputerVisionMetricsService computerVisionMetricsService;

    public ComputerVisionService(IConfiguration configuration, ComputerVisionMetricsService computerVisionMetricsService)
    {
        computerVisionClient = new ComputerVisionClient(new ApiKeyServiceClientCredentials(configuration.GetValue<string>("AppSettings:CognitiveServicesKey")))
        {
            Endpoint = configuration.GetValue<string>("AppSettings:CognitiveServicesEndpoint")
        };

        this.computerVisionMetricsService = computerVisionMetricsService;
    }

    public async Task<string?> GetDescriptionAsync(Stream stream)
    {
        stream.Position = 0;

        using var analyzeStream = new MemoryStream();
        await stream.CopyToAsync(analyzeStream);
        analyzeStream.Position = 0;

        computerVisionMetricsService.PayloadCounter.Add(analyzeStream.Length);
        computerVisionMetricsService.RequestCounter.Add(1);

        var result = await computerVisionClient.AnalyzeImageInStreamAsync(analyzeStream, new List<VisualFeatureTypes?> { VisualFeatureTypes.Description });

        var description = result.Description.Captions.FirstOrDefault();
        if (description != null)
        {
            computerVisionMetricsService.ConfidenceHistogram.Record(description.Confidence, KeyValuePair.Create<string, object?>("confidence-description", description.Text));
            return description.Text;
        }

        return null;
    }
}
