namespace SmartAssistant.Api.Options
{
    public class GeminiOptions
    {
        public const string SectionName = "Gemini";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gemini-2.5-flash";

        public string ApiVersion { get; set; } = "v1beta";
    }
}