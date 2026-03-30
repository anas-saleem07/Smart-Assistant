using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui;

namespace SmartAssistant.App
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTask,
        ConfigurationChanges = ConfigChanges.ScreenSize |
                               ConfigChanges.Orientation |
                               ConfigChanges.UiMode |
                               ConfigChanges.ScreenLayout |
                               ConfigChanges.SmallestScreenSize |
                               ConfigChanges.Density)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "smartassistant",
        DataHost = "google-auth-complete")]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            TryHandleDeepLink(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            TryHandleDeepLink(intent);
        }

        private static void TryHandleDeepLink(Intent? intent)
        {
            var action = intent?.Action;
            var dataString = intent?.DataString;

            if (action != Intent.ActionView || string.IsNullOrWhiteSpace(dataString))
                return;

            if (!Uri.TryCreate(dataString, UriKind.Absolute, out var uri))
                return;

            App.SetPendingDeepLink(uri.ToString());

            if (global::Microsoft.Maui.Controls.Application.Current != null)
                global::Microsoft.Maui.Controls.Application.Current.SendOnAppLinkRequestReceived(uri);
        }
    }
}