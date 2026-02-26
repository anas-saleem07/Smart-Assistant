using Microsoft.EntityFrameworkCore;
using SmartAssistant.Api.Data;
using SmartAssistant.Api.Services;
using SmartAssistant.Api.Services.Automation;
using SmartAssistant.Api.Services.Email;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAutoMapper(typeof(Program));
builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddScoped<IEmailClient, FakeEmailClient>(); // replace later with Gmail/Graph client
builder.Services.AddScoped<IReminderAutomationService, ReminderAutomationService>();

builder.Services.AddControllers();

//  Swagger (Swashbuckle)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Swagger JSON + UI
    app.UseSwagger();
    app.UseSwaggerUI();
    //app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();

//  Map controllers (recommended)
app.MapControllers();

// (Optional) if you REALLY want your custom route style, keep this instead of MapControllers():
// app.MapControllerRoute(
//     name: "default",
//     pattern: "api/{controller}/{action}/{id?}");

app.Run();