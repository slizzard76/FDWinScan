public class RecognizedRow
{
    public int RowNumber { get; set; }
    public Dictionary<string, string> Cells { get; set; } = new();
}