namespace SmartAssistant.Api.Options
{
    public sealed class GmailOAuthOptions
    {
        public const string SectionName = "GmailOAuth";

        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";

        // Windows desktop callback
        public string WindowsRedirectUri { get; set; } = "";

        // Android callback must be reachable from emulator/device browser
        public string AndroidRedirectUri { get; set; } = "";
    }
}