using System.ComponentModel.DataAnnotations;

namespace FDBackScan.Models;

/// <summary>Распознанное ключевое поле.</summary>
public class RecognizedField
{
    /// <summary>Название поля.</summary>
    /// <example>Номер документа</example>
    [Required]
    public string Name { get; set; } = "";

    /// <summary>Значение поля.</summary>
    /// <example>INV-2026-001</example>
    [Required]
    public string Value { get; set; } = "";

    /// <summary>Уверенность модели (0..1).</summary>
    /// <example>0.98</example>
    [Range(0, 1)]
    public double Confidence { get; set; }
}