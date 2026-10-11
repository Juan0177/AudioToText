using AudioToText.Web.Options;
using AudioToText.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var localSettingsPath = Path.Combine(builder.Environment.ContentRootPath, AppLocalSettingsStore.DefaultFileName);
builder.Configuration.AddJsonFile(localSettingsPath, optional: true, reloadOnChange: true);

builder.Services.Configure<CloudflareOptions>(
    builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.Configure<TranscriptionOptions>(
    builder.Configuration.GetSection(TranscriptionOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddSingleton<AudioUploadValidator>();
builder.Services.AddSingleton<AppLocalSettingsStore>();
builder.Services.AddSingleton<IFfmpegToolService, FfmpegToolService>();
builder.Services.AddSingleton<AudioPcmConverter>();
builder.Services.AddSingleton<ITldrService, ExtractiveTldrService>();
builder.Services.AddSingleton<ILocalWhisperRuntime, LocalWhisperRuntime>();
builder.Services.AddSingleton<FakeTranscriptionService>();
builder.Services.AddSingleton<LocalWhisperTranscriptionService>();
builder.Services.AddHttpClient("whisper-models", client =>
{
    client.Timeout = TimeSpan.FromHours(2);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AudioToText/1.0");
});
builder.Services.AddHttpClient("ffmpeg-tools", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AudioToText/1.0");
});
builder.Services.AddHttpClient<CloudflareWorkersAiTranscriptionService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddSingleton<ITranscriptionService, RoutingTranscriptionService>();

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
