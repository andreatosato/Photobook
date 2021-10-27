using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MimeMapping;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Photobook;
using Photobook.DataAccessLayer;
using Photobook.Filters;
using Photobook.Services;

const string SourceName = "Photobook";
var source = new ActivitySource(SourceName);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PhotoDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("SqlConnection")));
builder.Services.AddScoped<AzureStorageService>();
builder.Services.AddScoped<ComputerVisionService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.OperationFilter<ImageExtensionFilter>());

builder.Services.AddOpenTelemetryTracing(
    (builder) => builder
        .AddSource(SourceName)
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(SourceName).AddTelemetrySdk())
        .AddSqlClientInstrumentation(options =>
        {
            options.SetDbStatementForStoredProcedure = true;
            options.SetDbStatementForText = true;
            options.RecordException = true;
        })
        .AddAspNetCoreInstrumentation(options =>
        {
            options.Filter = (req) => !req.Request.Path.ToUriComponent().Contains("index.html", StringComparison.OrdinalIgnoreCase)
                && !req.Request.Path.ToUriComponent().Contains("swagger", StringComparison.OrdinalIgnoreCase);
        })
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new Uri("http://otel:4317");
        })
    );
builder.Services.Configure<AspNetCoreInstrumentationOptions>(options =>
{
    options.RecordException = true;
});

var app = builder.Build();
await EnsureDbAsync(app.Services);

app.UseHttpsRedirection();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = string.Empty;
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Photobook API v1");
});

app.MapGet("/photos", async (PhotoDbContext db) =>
{
    using var activity = source.StartActivity(SourceName, ActivityKind.Internal);

    var photos = await db.Photos.OrderBy(p => p.OriginalFileName).ToListAsync();

    activity?.AddEvent(new ActivityEvent("Load Photos", tags: new ActivityTagsCollection(new[] { KeyValuePair.Create<string, object?>("Count", photos.Count) })));
    activity?.SetTag("photoNumbers", photos.Count);
    activity?.SetTag("otel.status_code", "OK");
    activity?.SetTag("otel.status_description", "Load successfully");

    return photos;
})
.WithName(EndpointNames.GetPhotos);

app.MapGet("/photos/{id:guid}", async (Guid id, AzureStorageService azureStorageService, PhotoDbContext db) =>
{
    var photo = await db.Photos.FindAsync(id);
    if (photo is null)
    {
        return Results.NotFound();
    }

    var stream = await azureStorageService.ReadAsync(photo.Path);
    if (stream is null)
    {
        return Results.NotFound();
    }

    return Results.Stream(stream, MimeUtility.GetMimeMapping(photo.OriginalFileName));
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.WithName(EndpointNames.GetPhoto);

app.MapPost("photos", async (HttpRequest req, AzureStorageService storageService, ComputerVisionService computerVisionService, PhotoDbContext db) =>
{
    if (!req.HasFormContentType)
    {
        return Results.BadRequest();
    }

    var form = await req.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file is null || !file.IsImage())
    {
        return Results.BadRequest();
    }

    using var stream = file.OpenReadStream();

    var id = Guid.NewGuid();
    var newFileName = $"{id}{Path.GetExtension(file.FileName)}".ToLowerInvariant();
    var description = await computerVisionService.GetDescriptionAsync(stream);

    await storageService.SaveAsync(newFileName, stream);

    var photo = new Photo
    {
        Id = id,
        OriginalFileName = file.FileName,
        Path = newFileName,
        Description = description,
        UploadDate = DateTime.UtcNow
    };

    db.Photos.Add(photo);
    await db.SaveChangesAsync();

    return Results.NoContent();
})
.WithName(EndpointNames.UploadPhoto)
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status400BadRequest);

app.MapDelete("/photos/{id:guid}", async (Guid id, AzureStorageService storageService, PhotoDbContext db) =>
{
    var photo = await db.Photos.FindAsync(id);
    if (photo is null)
    {
        return Results.NotFound();
    }

    await storageService.DeleteAsync(photo.Path);

    db.Photos.Remove(photo);
    await db.SaveChangesAsync();

    return Results.NoContent();
})
.WithName(EndpointNames.DeletePhoto)
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound);

app.Run();

async Task EnsureDbAsync(IServiceProvider services)
{
    using var activity = source.StartActivity("DatabaseMigration", ActivityKind.Internal);

    using var db = services.CreateScope().ServiceProvider.GetRequiredService<PhotoDbContext>();
    await db.Database.MigrateAsync();

    activity?.Stop();
}