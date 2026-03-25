namespace SmartAssistant.Api.Models
{
    public sealed class GoogleConnectionStatusModel
    {
        public bool IsConnected { get; set; }
        public bool NeedsReconnect { get; set; }
        public string Provider { get; set; } = "Gmail";
        public string Email { get; set; } = "";
        public string Message { get; set; } = "";
    }
}