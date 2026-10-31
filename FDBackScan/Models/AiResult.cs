namespace FDBackScan.Models
{
    public class AiResult
    {
        public List<RecognizedField> Fields { get; set; } = new();
        public List<RecognizedRow> Rows { get; set; } = new();
    }
}
