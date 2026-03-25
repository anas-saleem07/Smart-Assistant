namespace SmartAssistant.App
{
    public partial class App : Application
    {
        private static string? _pendingDeepLink;

        public App()
        {
            InitializeComponent();
            MainPage = new MainPage();
            MainPage = new MainPage();
        }

        protected override void OnAppLinkRequestReceived(Uri uri)
        {
            base.OnAppLinkRequestReceived(uri);

            _pendingDeepLink = uri?.ToString();
        }

        public static string? ConsumePendingDeepLink()
        {
            var value = _pendingDeepLink;
            _pendingDeepLink = null;
            return value;
        }
    }
}