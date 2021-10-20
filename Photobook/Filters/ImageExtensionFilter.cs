using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Photobook.Filters;

public class ImageExtensionFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.OperationId == EndpointNames.UploadPhoto)
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Description = "File to upload",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    {
                        "multipart/form-data", new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Required = new HashSet<string>{ "file" },
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    {
                                        "file", new OpenApiSchema()
                                        {
                                            Type = "string",
                                            Format = "binary",
                                            Extensions = new Dictionary<string, IOpenApiExtension>
                                            {
                                              { "x-ms-media-kind", new OpenApiString("image")}
                                            }
                                        }
                                    }
                                },
                            }
                        }
                    }
                }
            };
        }
    }
}