using Foundation;

namespace SmartAssistant.App
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool OpenUrl(UIKit.UIApplication app, NSUrl url, NSDictionary options)
        {
            if (url != null && Uri.TryCreate(url.AbsoluteString, UriKind.Absolute, out var uri))
            {
                App.Current?.SendOnAppLinkRequestReceived(uri);
                return true;
            }

            return base.OpenUrl(app, url, options);
        }
    }
}