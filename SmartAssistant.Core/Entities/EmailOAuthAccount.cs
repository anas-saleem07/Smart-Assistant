namespace SmartAssistant.Core.Entities
{
    public class EmailOAuthAccount
    {
        public long Id { get; set; }
        public string Provider { get; set; } = "Gmail";
        public string Email { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string RefreshToken { get; set; } = default!;
        public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
        public bool Active { get; set; } = true;
        public DateTimeOffset? UpdatedOn { get; set; }
    }
}