using Microsoft.EntityFrameworkCore;
using Photobook;
using Photobook.DataAccessLayer;
using Photobook.Exntensions;
using Photobook.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PhotoDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlConnection")));

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

app.MapGet("/photos", async (PhotoDbContext db) =>
{
    var photos = await db.Photos.OrderBy(p => p.OriginalFileName).ToListAsync();
    return photos;
})
.WithName(EndpointNames.GetPhotos);

app.MapGet("/photos/{id:guid}", async (PhotoDbContext db) =>
{
    // TODO: Downoload the photo
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.WithName(EndpointNames.GetPhoto);

app.MapPost("photos", async (HttpRequest req, PhotoDbContext db) =>
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

    // TODO: Call Computer Vision to get photo description and save it to Azure Storage.

    var id = Guid.NewGuid();
    var newFileName = $"{id}{Path.GetExtension(file.FileName)}";

    var photo = new Photo
    {
        Id = id,
        OriginalFileName = file.FileName,
        Path = $"https://storage/{newFileName}",
        UploadDate = DateTime.UtcNow
    };

    db.Photos.Add(photo);
    await db.SaveChangesAsync();

    return Results.NoContent();
})
.WithName(EndpointNames.UploadPhoto)
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status400BadRequest);

app.MapDelete("/photos/{id:guid}", async (Guid id, PhotoDbContext db) =>
{
    var photo = await db.Photos.FindAsync(id);
    if (photo is null)
    {
        return Results.NotFound();
    }

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
    using var db = services.CreateScope().ServiceProvider.GetRequiredService<PhotoDbContext>();
    await db.Database.MigrateAsync();
}