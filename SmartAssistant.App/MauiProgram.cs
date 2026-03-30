using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

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
                })
                .ConfigureLifecycleEvents(lifecycle =>
                {
#if ANDROID
                    lifecycle.AddAndroid(android =>
                    {
                        android.OnCreate((activity, bundle) =>
                        {
                            var action = activity.Intent?.Action;
                            var data = activity.Intent?.Data?.ToString();

                            if (action == Android.Content.Intent.ActionView &&
                                !string.IsNullOrWhiteSpace(data) &&
                                Uri.TryCreate(data, UriKind.Absolute, out var uri))
                            {
                                App.Current?.SendOnAppLinkRequestReceived(uri);
                            }
                        });

                        android.OnNewIntent((activity, intent) =>
                        {
                            var action = intent?.Action;
                            var data = intent?.Data?.ToString();

                            if (action == Android.Content.Intent.ActionView &&
                                !string.IsNullOrWhiteSpace(data) &&
                                Uri.TryCreate(data, UriKind.Absolute, out var uri))
                            {
                                App.Current?.SendOnAppLinkRequestReceived(uri);
                            }
                        });
                    });
#endif
                });

            builder.Services.AddMauiBlazorWebView();

            var baseAddress =
    DeviceInfo.Platform == DevicePlatform.Android
        ? "http://10.0.2.2:5256/"
        : "https://localhost:7151/";

            builder.Services.AddScoped(_ => new HttpClient
            {
                BaseAddress = new Uri(baseAddress),
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