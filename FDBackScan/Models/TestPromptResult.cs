namespace FDBackScan.Models
{
    /// <summary>Результат тестового распознавания промпта.</summary>
    public class TestPromptResult
    {
        /// <summary>Промпт, который был проверен.</summary>
        public string Prompt { get; set; } = "";

        /// <summary>Сколько ключевых полей вернула модель.</summary>
        public int FieldCount { get; set; }

        /// <summary>Сколько строк таблицы вернула модель.</summary>
        public int RowCount { get; set; }

        /// <summary>Время обработки запроса (мс).</summary>
        public long ElapsedMs { get; set; }

        /// <summary>Первые 5 полей — для быстрого просмотра.</summary>
        public List<RecognizedField> SampleFields { get; set; } = new();

        /// <summary>Первые 3 строки таблицы — для быстрого просмотра.</summary>
        public List<RecognizedRow> SampleRows { get; set; } = new();
    }
}
