using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Diagnostics.Metrics;

namespace Photobook.Services;

public class ComputerVisionService
{
    private readonly ComputerVisionClient computerVisionClient;
    private readonly Meter meter;

    public ComputerVisionService(IConfiguration configuration)
    {
        computerVisionClient = new ComputerVisionClient(new ApiKeyServiceClientCredentials(configuration.GetValue<string>("AppSettings:CognitiveServicesKey")))
        {
            Endpoint = configuration.GetValue<string>("AppSettings:CognitiveServicesEndpoint")
        };
        meter = new Meter("ComputerVision");
    }

    public async Task<string?> GetDescriptionAsync(Stream stream)
    {
        stream.Position = 0;

        using var analyzeStream = new MemoryStream();
        await stream.CopyToAsync(analyzeStream);
        analyzeStream.Position = 0;


        Counter<int> payloadMetrics = meter.CreateCounter<int>("PayloadCounter");
        payloadMetrics.Add((int)analyzeStream.Length);
        Counter<int> cognitiveRequest = meter.CreateCounter<int>("CognitiveRequest");
        cognitiveRequest.Add(1);

        var result = await computerVisionClient.AnalyzeImageInStreamAsync(analyzeStream, new List<VisualFeatureTypes?> { VisualFeatureTypes.Description, VisualFeatureTypes.Tags, VisualFeatureTypes.Faces });

        Histogram<double> confidence = meter.CreateHistogram<double>("Confidence");
        confidence.Record(result.Description.Captions.FirstOrDefault()?.Confidence ?? 0, KeyValuePair.Create<string, object?>("confidence-description", result.Description.Captions.FirstOrDefault()?.Text));

        return result.Description.Captions.FirstOrDefault()?.Text;
    }
}
