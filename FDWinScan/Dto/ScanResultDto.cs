public class ScanResultDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public DateTime ScannedAt { get; set; }
    public List<RecognizedField> Fields { get; set; } = new();
    public List<RecognizedRow> Rows { get; set; } = new();
    public string? Prompt { get; set; }
}