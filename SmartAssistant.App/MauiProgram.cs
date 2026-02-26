using Microsoft.Extensions.Logging;

namespace SmartAssistant.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // IMPORTANT:
            // Your API launchSettings uses:
            // HTTP  -> http://localhost:5256
            // HTTPS -> https://localhost:7151
            // Use HTTP to avoid SSL dev-cert issues in MAUI.
            builder.Services.AddScoped(_ => new HttpClient
            {
                BaseAddress = new Uri("http://localhost:5256/"),
                Timeout = TimeSpan.FromSeconds(8)
            });

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}