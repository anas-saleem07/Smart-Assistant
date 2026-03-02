namespace SmartAssistant.Api.Options
{
    public sealed class GmailOAuthOptions
    {
        public const string SectionName = "GmailOAuth";

        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
    }
}