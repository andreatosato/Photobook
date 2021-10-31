using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace Photobook.Services;

public class ComputerVisionService
{
    private readonly ComputerVisionClient computerVisionClient;

    public ComputerVisionService(IConfiguration configuration)
    {
        computerVisionClient = new ComputerVisionClient(new ApiKeyServiceClientCredentials(configuration.GetValue<string>("AppSettings:CognitiveServicesKey")))
        {
            Endpoint = configuration.GetValue<string>("AppSettings:CognitiveServicesEndpoint")
        };
    }

    public async Task<string?> GetDescriptionAsync(Stream stream)
    {
        stream.Position = 0;

        using var analyzeStream = new MemoryStream();
        await stream.CopyToAsync(analyzeStream);
        analyzeStream.Position = 0;

        var result = await computerVisionClient.AnalyzeImageInStreamAsync(analyzeStream, new List<VisualFeatureTypes?> { VisualFeatureTypes.Description });

        MeterListener listener = new MeterListener();
        Meter meter = new Meter("ComputerVision");

        Histogram<int> histogram = meter.CreateHistogram<int>("VisionMetrics");
        //listener.EnableMeasurementEvents(histogram);
        //listener.Start();

        histogram.Record(result.Description.Tags.Count, KeyValuePair.Create<string, object>("tags", JsonSerializer.Serialize(result.Description.Tags)));
        if (result.Tags != null)
            histogram.Record(result.Tags.Count, KeyValuePair.Create<string, object>("image.tags", JsonSerializer.Serialize(result.Tags)));
        if (result.Faces != null)
            histogram.Record(result.Faces.Count, KeyValuePair.Create<string, object>("categories", JsonSerializer.Serialize(result.Faces)));

        return result.Description.Captions.FirstOrDefault()?.Text;
    }
}
