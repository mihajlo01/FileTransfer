using FileTransfer.Server.BusinessLogic.Interfaces;
using FileTransfer.Server.BusinessLogic.Services;
using FileTransfer.Server.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = null;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = long.MaxValue;
});

builder.Services.Configure<UploadSettings>(builder.Configuration.GetSection("UploadSettings"));

builder.Services.AddControllers();
builder.Services.AddScoped<IUploadsService, UploadsService>();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
