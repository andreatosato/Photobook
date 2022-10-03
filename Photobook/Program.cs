using Microsoft.EntityFrameworkCore;
using MimeMapping;
using Photobook;
using Photobook.DataAccessLayer;
using Photobook.Filters;
using Photobook.Logging;
using Photobook.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ProgramLogger>();

builder.Services.AddSqlServer<PhotoDbContext>(builder.Configuration.GetConnectionString("SqlConnection"));
builder.Services.AddScoped<AzureStorageService>();
builder.Services.AddScoped<ComputerVisionService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.OperationFilter<ImageExtensionFilter>());

var app = builder.Build();

await EnsureDbAsync(app.Services);

app.UseHttpsRedirection();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = string.Empty;
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Photobook API v1");
});

app.MapGet("/photos", async (PhotoDbContext db, ProgramLogger logger) =>
{
    logger.LogRequestMapGetPhotos();
    var photos = await db.Photos.OrderBy(p => p.OriginalFileName).ToListAsync();

    logger.LogResponseMapGetPhotos(photos.Count);
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

    return Results.CreatedAtRoute(EndpointNames.GetPhoto, new { id }, photo);
})
.WithName(EndpointNames.UploadPhoto)
.Produces(StatusCodes.Status201Created)
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

static async Task EnsureDbAsync(IServiceProvider services)
{
    using var db = services.CreateScope().ServiceProvider.GetRequiredService<PhotoDbContext>();
    await db.Database.MigrateAsync();
}