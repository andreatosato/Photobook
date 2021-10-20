using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

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
        return result.Description.Captions.FirstOrDefault()?.Text;
    }
}
