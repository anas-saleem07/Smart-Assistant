namespace SmartAssistant.App
{
    public partial class App : Application
    {
        private static string? _pendingDeepLink;

        public static event Action? PendingDeepLinkChanged;

        public App()
        {
            InitializeComponent();
            MainPage = new MainPage();
        }

        protected override void OnAppLinkRequestReceived(Uri uri)
        {
            base.OnAppLinkRequestReceived(uri);
            SetPendingDeepLink(uri?.ToString());
        }

        public static void SetPendingDeepLink(string? url)
        {
            _pendingDeepLink = url;
            PendingDeepLinkChanged?.Invoke();
        }

        public static string? ConsumePendingDeepLink()
        {
            var value = _pendingDeepLink;
            _pendingDeepLink = null;
            return value;
        }
    }
}