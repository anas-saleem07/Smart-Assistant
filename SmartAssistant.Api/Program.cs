using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Jobs;
using SmartAssistant.Api.Options;
using SmartAssistant.Api.Services;
using SmartAssistant.Api.Services.Automation;
using SmartAssistant.Api.Services.AutoReply;
using SmartAssistant.Api.Services.Email;
using SmartAssistant.Api.Services.Google;
using SmartAssistant.Api.Services.Settings;

var builder = WebApplication.CreateBuilder(args);

// --------------------
// Database
// --------------------
builder.Services.AddDbContext<ApplicationDbContext>(dbOptions =>
{
    dbOptions.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"));
});

// --------------------
// Hangfire (Storage + Server)
// --------------------
builder.Services.AddHangfire(config =>
{
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
    config.UseSimpleAssemblyNameTypeSerializer();
    config.UseRecommendedSerializerSettings();

    config.UseSqlServerStorage(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new SqlServerStorageOptions
        {
            CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
            SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
            QueuePollInterval = TimeSpan.FromSeconds(15),
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks = true
        });
});

builder.Services.AddHangfireServer();

// --------------------
// Options (Gmail OAuth)
// --------------------
builder.Services
    .AddOptions<GmailOAuthOptions>()
    .BindConfiguration(GmailOAuthOptions.SectionName)
    .Validate(o =>
        !string.IsNullOrWhiteSpace(o.ClientId) &&
        !string.IsNullOrWhiteSpace(o.ClientSecret) &&
        !string.IsNullOrWhiteSpace(o.WindowsRedirectUri) &&
        !string.IsNullOrWhiteSpace(o.AndroidRedirectUri),
        "GmailOAuth settings are missing.")
    .ValidateOnStart();

// --------------------
// Gemini API options
// --------------------
builder.Services
    .AddOptions<GeminiOptions>()
    .BindConfiguration(GeminiOptions.SectionName)
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.ApiKey),
        "Gemini ApiKey missing")
    .ValidateOnStart();

builder.Services.AddHttpClient<
    SmartAssistant.Api.Services.Ai.IAiClient,
    SmartAssistant.Api.Services.Ai.GeminiClient>();

// --------------------
// App services
// --------------------
builder.Services.AddAutoMapper(typeof(Program));

builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddScoped<IReminderAutomationService, ReminderAutomationService>();

// Register Hangfire job once only.
builder.Services.AddScoped<ReminderAutomationJob>();
builder.Services.AddScoped<AutoReplyPendingJob>();

builder.Services.AddScoped<IAutoReplyService, AutoReplyService>();

// Register OAuthTokenHelper once only.
builder.Services.AddScoped<IOAuthTokenHelper, OAuthTokenHelper>();

builder.Services.AddScoped<
    IGoogleConnectionService,
    GoogleConnectionService>();

builder.Services.AddScoped<
    IReminderSettingService,
    ReminderSettingsService>();

builder.Services.AddScoped<
    SmartAssistant.Api.Services.Calendar.ICalendarService,
    SmartAssistant.Api.Services.Calendar.GoogleCalendarService>();

// IMPORTANT: register ONLY ONE IEmailClient
builder.Services.AddScoped<IEmailClient, GmailEmailClient>();

builder.Services.AddScoped<IEmailOAuthService, EmailOAuthService>();

// --------------------
// MVC + Swagger
// --------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --------------------
// HTTP pipeline
// --------------------

// Swagger is available locally and in the hosted FYP environment.
if (app.Environment.IsDevelopment() ||
    app.Environment.IsEnvironment("FypDemo"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Keep Hangfire dashboard only for local development.
// Do not expose it publicly on the hosted FYP API.
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}

// NOTE:
// HTTPS redirection is disabled in Development to allow Android emulator
// (10.0.2.2) to call the API over HTTP without SSL certificate issues.
// In FypDemo / Production, HTTPS remains enabled.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

// --------------------
// Hangfire recurring job registration
// --------------------
// Use Hangfire's DI-based recurring job manager.
//
// Do NOT use the static RecurringJob.AddOrUpdate API here.
// On the hosted environment JobStorage.Current may not yet be initialized,
// which caused the application startup failure:
//
// "Current JobStorage instance has not been initialized yet."
//
// Resolving IRecurringJobManager from DI ensures it uses the Hangfire
// SQL storage configured above through AddHangfire().
var recurringJobManager =
    app.Services.GetRequiredService<IRecurringJobManager>();

recurringJobManager.AddOrUpdate<ReminderAutomationJob>(
    "ReminderAutomationJob",
    job => job.Run(CancellationToken.None),
    "*/10 * * * *" // every 10 minutes
);

recurringJobManager.AddOrUpdate<AutoReplyPendingJob>(
    "AutoReplyPendingJob",
    job => job.Run(CancellationToken.None),
    "*/10 * * * *"
);

app.Run();