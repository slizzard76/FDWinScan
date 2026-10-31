using FDBackScan.Models;
using System.ComponentModel.DataAnnotations;

namespace FDBackScan.Models;

/// <summary>
/// Результат распознавания документа.
/// </summary>
public class ScanResultDto
{
    /// <summary>Идентификатор сессии сканирования.</summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    [Required]
    public Guid Id { get; set; }

    /// <summary>Имя загруженного файла.</summary>
    /// <example>invoice.png</example>
    [Required]
    public string FileName { get; set; } = "";

    /// <summary>MIME-тип файла.</summary>
    /// <example>image/png</example>
    [Required]
    public string ContentType { get; set; } = "";

    /// <summary>Время распознавания (UTC).</summary>
    /// <example>2026-10-30T12:00:00Z</example>
    public DateTime ScannedAt { get; set; }

    /// <summary>Промпт, использованный при распознавании.</summary>
    public string? Prompt { get; set; }

    /// <summary>Ключевые поля, извлечённые из документа.</summary>
    public List<RecognizedField> Fields { get; set; } = new();

    /// <summary>Табличные данные (позиции накладной, чека и т.п.).</summary>
    public List<RecognizedRow> Rows { get; set; } = new();
}