using AudioToText.Web.Options;
using AudioToText.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CloudflareOptions>(
    builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.Configure<TranscriptionOptions>(
    builder.Configuration.GetSection(TranscriptionOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddSingleton<AudioUploadValidator>();

var provider = builder.Configuration.GetValue<string>("Transcription:Provider") ?? "CloudflareRest";
if (string.Equals(provider, "Fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ITranscriptionService, FakeTranscriptionService>();
}
else
{
    builder.Services.AddHttpClient<ITranscriptionService, CloudflareWorkersAiTranscriptionService>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(3);
    });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error.html");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
