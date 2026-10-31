namespace FDWinScan.Configuration;
public class RecognitionSettings
{
    public const string DefaultPrompt =
        "Распознай документ. Извлеки ключевые поля (номер, дата, сумма, контрагент) " +
        "и табличные данные (наименование, количество, цена, итого). " +
        "Верни структурированный JSON.";

    public string Prompt { get; set; } = DefaultPrompt;
}
