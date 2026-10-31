namespace FDBackScan.Configuration
{
    public class AiSettings
    {
        public const string SectionName = "AI";

        /// <summary>Активный провайдер: "Ollama", "OpenAI" или "AzureDocumentIntelligence".</summary>
        public string Provider { get; set; } = "Ollama";

        public OllamaSettings Ollama { get; set; } = new();
        public OpenAiSettings OpenAi { get; set; } = new();
        public AzureDocIntelligenceSettings AzureDocIntelligence { get; set; } = new();
    }

    public class OllamaSettings
    {
        public string BaseUrl { get; set; } = "http://localhost:11434/";
        public string Model { get; set; } = "mistral-small3.1";
        public int TimeoutSeconds { get; set; } = 300;
        public double Temperature { get; set; } = 0.1;
    }

    public class OpenAiSettings
    {
        public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o";
        public int TimeoutSeconds { get; set; } = 120;
        public double Temperature { get; set; } = 0.1;
    }

    public class AzureDocIntelligenceSettings
    {
        public string Endpoint { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ModelId { get; set; } = "prebuilt-invoice";
    }
}
