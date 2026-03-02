using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Jobs;
using SmartAssistant.Api.Options;
using SmartAssistant.Api.Services;
using SmartAssistant.Api.Services.Automation;
using SmartAssistant.Api.Services.Email;

var builder = WebApplication.CreateBuilder(args);

// --------------------
// Database
// --------------------
builder.Services.AddDbContext<ApplicationDbContext>(dbOptions =>
{
    dbOptions.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
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
        !string.IsNullOrWhiteSpace(o.RedirectUri),
        "GmailOAuth settings are missing.")
    .ValidateOnStart();

// --------------------
// App services
// --------------------
builder.Services.AddAutoMapper(typeof(Program));

builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddScoped<IReminderAutomationService, ReminderAutomationService>();
builder.Services.AddScoped<ReminderAutomationJob>();

//       IMPORTANT: register ONLY ONE IEmailClient
builder.Services.AddScoped<IEmailClient, GmailEmailClient>();

builder.Services.AddScoped<IEmailOAuthService, EmailOAuthService>();

//  A small wrapper job class (best practice for Hangfire)
builder.Services.AddScoped<ReminderAutomationJob>();

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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    //  Dashboard only in Development (simple + safe for FYP)
    app.UseHangfireDashboard("/hangfire");
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// --------------------
// Hangfire recurring job registration
// --------------------
RecurringJob.AddOrUpdate<ReminderAutomationJob>(
    "ReminderAutomationJob",
    job => job.Run(CancellationToken.None),
    "*/10 * * * *" // every 10 minutes
);

app.Run();


// --------------------
// Job wrapper (clean)
// --------------------
//public sealed class ReminderAutomationJob
//{
//    private readonly IReminderAutomationService _automation;

//    public ReminderAutomationJob(IReminderAutomationService automation)
//    {
//        _automation = automation;
//    }

//    public Task Run(CancellationToken ct)
//    {
//        // Hangfire will record success/failure + retries
//        return _automation.ScanAndCreateRemindersAsync(ct);
//    }
//}