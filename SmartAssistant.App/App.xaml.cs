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

        protected override void OnStart() => ForegroundState.IsActive = true;
        protected override void OnResume() => ForegroundState.IsActive = true;
        protected override void OnSleep() => ForegroundState.IsActive = false;

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = base.CreateWindow(activationState);
            window.Activated += (_, _) => ForegroundState.IsActive = true;
            window.Deactivated += (_, _) => ForegroundState.IsActive = false;
            return window;
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
