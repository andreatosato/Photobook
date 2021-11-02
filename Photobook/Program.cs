using Microsoft.EntityFrameworkCore;
using MimeMapping;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Photobook;
using Photobook.DataAccessLayer;
using Photobook.Filters;
using Photobook.Services;
using System.Diagnostics;
using System.Text.Json;

const string SourceName = "Photobook";
const string MeterName = "ComputerVision";
var source = new ActivitySource(SourceName);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PhotoDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("SqlConnection")));
builder.Services.AddScoped<AzureStorageService>();
builder.Services.AddScoped<ComputerVisionService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.OperationFilter<ImageExtensionFilter>());

builder.Services.AddOpenTelemetryTracing(options =>
     options
        .AddSource(SourceName)
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(SourceName).AddTelemetrySdk())
        .AddSqlClientInstrumentation(options =>
        {
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
            otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("AppSettings:OtelEndpoint"));
        })
    );

builder.Services.AddOpenTelemetryMetrics(options =>
    options.AddHttpClientInstrumentation()
     .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(SourceName).AddTelemetrySdk())
     .AddMeter(MeterName)
     .AddOtlpExporter(otlpOptions =>
     {
         otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("AppSettings:OtelEndpoint"));
     })
);

builder.Services.Configure<AspNetCoreInstrumentationOptions>(options =>
{
    options.RecordException = true;
});

builder.Services.AddSingleton<ComputerVisionMetricsService>();

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
    using var activity = source.StartActivity(SourceName, ActivityKind.Internal)!;

    var photos = await db.Photos.OrderBy(p => p.OriginalFileName).ToListAsync();

    activity.AddEvent(new ActivityEvent("Load Photos", tags: new ActivityTagsCollection(new[] { KeyValuePair.Create<string, object?>("Count", photos.Count) })));
    activity.SetTag("photoNumbers", photos.Count);
    activity.SetTag("otel.status_code", "OK");
    activity.SetTag("otel.status_description", "Load successfully");

    return photos;
})
.WithName(EndpointNames.GetPhotos);

app.MapGet("/photos/{id:guid}", async (Guid id, AzureStorageService azureStorageService, PhotoDbContext db) =>
{
    using var getActivity = source.StartActivity(SourceName, ActivityKind.Internal)!;

    var dbActivity = source.StartActivity(SourceName, ActivityKind.Consumer, getActivity.Context)!;

    var photo = await db.Photos.FindAsync(id);
    if (photo is null)
    {
        dbActivity.SetStatus(Status.Error);
        return Results.NotFound();
    }

    dbActivity.SetTag("mimetype", MimeUtility.GetMimeMapping(photo.OriginalFileName));
    dbActivity.Stop();

    var streamActivity = source.StartActivity(SourceName, ActivityKind.Consumer, getActivity.Context)!;
    var stream = await azureStorageService.ReadAsync(photo.Path);
    if (stream is null)
    {
        streamActivity.SetStatus(Status.Error);
        return Results.NotFound();
    }

    streamActivity.SetTag("stream.dimension", stream.Length);
    streamActivity.Stop();

    return Results.Stream(stream, MimeUtility.GetMimeMapping(photo.OriginalFileName));
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.WithName(EndpointNames.GetPhoto);

app.MapPost("photos", async (HttpRequest req, AzureStorageService storageService, ComputerVisionService computerVisionService, PhotoDbContext db) =>
{
    using var postActivity = source.StartActivity(SourceName, ActivityKind.Internal)!;

    if (!req.HasFormContentType)
    {
        postActivity.SetStatus(Status.Error);
        return Results.BadRequest();
    }

    var readStreamActivity = source.StartActivity(SourceName, ActivityKind.Consumer, postActivity.Context)!;
    readStreamActivity.DisplayName = "ReadStream";

    var form = await req.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    readStreamActivity.SetTag("file-numbers", form.Files.Count);

    if (file is null || !file.IsImage())
    {
        readStreamActivity.SetStatus(Status.Error);
        return Results.BadRequest();
    }

    using var stream = file.OpenReadStream();

    var id = Guid.NewGuid();
    var newFileName = $"{id}{Path.GetExtension(file.FileName)}".ToLowerInvariant();

    readStreamActivity.Stop();

    var computerVisionActivity = source.StartActivity(SourceName, ActivityKind.Consumer, postActivity.Context)!;
    computerVisionActivity.DisplayName = "Computer Vision";

    var description = await computerVisionService.GetDescriptionAsync(stream);

    computerVisionActivity.SetTag("description", description);
    computerVisionActivity.Stop();

    var storageActivity = source.StartActivity(SourceName, ActivityKind.Consumer, postActivity.Context)!;
    storageActivity.DisplayName = "Storage Activity";

    await storageService.SaveAsync(newFileName, stream);
    storageActivity.Stop();

    var dbActivity = source.StartActivity(SourceName, ActivityKind.Consumer, postActivity.Context)!;
    dbActivity.DisplayName = "Entity Framework Core Activity";

    var photo = new Photo
    {
        Id = id,
        OriginalFileName = file.FileName,
        Path = newFileName,
        Description = description,
        UploadDate = DateTime.UtcNow
    };

    dbActivity.SetTag("entity", JsonSerializer.Serialize(photo));

    db.Photos.Add(photo);
    await db.SaveChangesAsync();

    dbActivity.Stop();

    return Results.CreatedAtRoute(EndpointNames.GetPhoto, new { id }, photo);
})
.WithName(EndpointNames.UploadPhoto)
.Produces(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest);

app.MapDelete("/photos/{id:guid}", async (Guid id, AzureStorageService storageService, PhotoDbContext db) =>
{
    using var deleteActivity = source.StartActivity(SourceName, ActivityKind.Internal)!;
    deleteActivity.SetTag("photo-id", id);
    var photo = await db.Photos.FindAsync(id);
    if (photo is null)
    {
        deleteActivity.SetStatus(Status.Error);
        return Results.NotFound();
    }

    var storageActivity = source.StartActivity(SourceName, ActivityKind.Consumer, deleteActivity.Context)!;
    storageActivity.DisplayName = "Storage Activity";

    await storageService.DeleteAsync(photo.Path);
    storageActivity.Stop();

    var dbActivity = source.StartActivity(SourceName, ActivityKind.Consumer, deleteActivity.Context)!;
    dbActivity.DisplayName = "Entity Framework Core Activity";
    dbActivity.SetTag("photo", photo);

    db.Photos.Remove(photo);
    await db.SaveChangesAsync();

    dbActivity.Stop();

    return Results.NoContent();
})
.WithName(EndpointNames.DeletePhoto)
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound);

app.Run();

async Task EnsureDbAsync(IServiceProvider services)
{
    using var db = services.CreateScope().ServiceProvider.GetRequiredService<PhotoDbContext>();
    await db.Database.MigrateAsync();
}