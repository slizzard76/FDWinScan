using System.ComponentModel.DataAnnotations;

namespace FDBackScan.Models;

/// <summary>Строка таблицы распознанных данных.</summary>
public class RecognizedRow
{
    /// <summary>Номер строки в документе.</summary>
    /// <example>1</example>
    public int RowNumber { get; set; }

    /// <summary>Ячейки строки: имя колонки — значение.</summary>
    /// <example>{ "Наименование": "Услуга А", "Кол-во": "1" }</example>
    [Required]
    public Dictionary<string, string> Cells { get; set; } = new();
}