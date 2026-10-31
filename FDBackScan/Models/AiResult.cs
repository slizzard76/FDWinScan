namespace FDBackScan.Models
{
    public class AiResult
    {
        public List<RecognizedField> Fields { get; set; } = new();
        public List<RecognizedRow> Rows { get; set; } = new();
    }



    public class Message
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public string Thinking { get; set; }
    }

    public class Root
    {
        public string model { get; set; }
        public DateTime created_at { get; set; }
        public Message message { get; set; }
        public bool done { get; set; }
        public string done_reason { get; set; }
        public long total_duration { get; set; }
        public long load_duration { get; set; }
        public int prompt_eval_count { get; set; }
        public int prompt_eval_cached_count { get; set; }
        public int prompt_eval_duration { get; set; }
        public int eval_count { get; set; }
        public long eval_duration { get; set; }
    }
}
