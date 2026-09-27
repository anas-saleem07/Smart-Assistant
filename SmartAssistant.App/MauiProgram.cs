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

            // DEBUG:
            // Android uses the Visual Studio Dev Tunnel.
            // Windows uses the locally running API.
            //
            // RELEASE:
            // Android and Windows both use the hosted FYP API.
//#if DEBUG
//            var baseAddress =
//                DeviceInfo.Platform == DevicePlatform.Android
//                    ? "https://h4n4qd3b-7151.inc1.devtunnels.ms/"
//                    : "https://localhost:7151/";
//#else
            var baseAddress = "https://assistflow.runasp.net/";
//#endif

            // Register one HttpClient for API communication.
            builder.Services.AddScoped(_ => new HttpClient
            {
                BaseAddress = new Uri(baseAddress),
                Timeout = TimeSpan.FromSeconds(120)
            });

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}