using ClosedXML.Excel;
using FDBackScan.Services;
using Microsoft.AspNetCore.Mvc;
using FDBackScan.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FDBackScan.Controllers;

/// <summary>
/// Сканирование документов, распознавание через ИИ и экспорт в Excel.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ScanController : ControllerBase
{
    private const string DefaultPrompt =
        "Распознай документ. Извлеки ключевые поля (номер, дата, сумма, контрагент) " +
        "и табличные данные (наименование, количество, цена, итого). " +
        "Верни структурированный JSON.";

    private readonly IScanStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScanController> _logger;

    public ScanController(
        IScanStore store,
        IHttpClientFactory httpClientFactory,
        ILogger<ScanController> logger)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Загружает изображение документа, распознаёт его с помощью ИИ
    /// и возвращает структурированные данные.
    /// </summary>
    /// <remarks>
    /// Пример запроса:
    ///
    ///     POST /api/scan/process
    ///     Content-Type: multipart/form-data
    ///     file:   (binary)
    ///     prompt: (string, опционально)
    ///
    /// В ответе возвращается идентификатор сессии (<c>id</c>), который используется
    /// для получения изображения (<c>/api/scan/{id}/image</c>) и Excel-файла (<c>/api/scan/{id}/excel</c>).
    /// </remarks>
    /// <param name="file">Изображение документа (JPG, PNG, TIFF).</param>
    /// <param name="prompt">Промпт для ИИ. Если не задан — используется промпт по умолчанию.</param>
    /// <response code="200">Документ успешно распознан.</response>
    /// <response code="400">Файл не передан, пустой или имеет неверный формат.</response>
    /// <response code="500">Ошибка при обращении к сервису ИИ.</response>
    [HttpPost("process")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ScanResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ScanResultDto>> Process(
        IFormFile file,
        string? prompt = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не загружен." });

        if (!IsSupportedContentType(file.ContentType))
            return BadRequest(new { error = $"Неподдерживаемый формат файла: {file.ContentType}." });

        if (string.IsNullOrWhiteSpace(prompt))
            prompt = DefaultPrompt;

        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }

        AiResult recognized;
        try
        {
            recognized = await CallAiServiceAsync(imageBytes, file.ContentType, prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при вызове ИИ для файла {FileName}", file.FileName);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Ошибка при распознавании документа." });
        }

        var id = Guid.NewGuid();
        var dto = new ScanResultDto
        {
            Id = id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            ScannedAt = DateTime.UtcNow,
            Prompt = prompt,
            Fields = recognized.Fields,
            Rows = recognized.Rows
        };

        _store.Save(id, imageBytes, file.ContentType, dto);
        _logger.LogInformation("Документ {FileName} распознан, ID={Id}", file.FileName, id);

        return Ok(dto);
    }

    /// <summary>
    /// Возвращает исходное изображение отсканированного документа.
    /// </summary>
    /// <param name="id">Идентификатор сессии сканирования.</param>
    /// <response code="200">Изображение найдено.</response>
    /// <response code="404">Сессия с указанным ID не найдена.</response>
    [HttpGet("{id:guid}/image")]
    [Produces("image/jpeg", "image/png", "image/tiff", "image/bmp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetImage(Guid id)
    {
        var data = _store.GetImage(id);
        if (data == null)
            return NotFound(new { error = $"Сессия {id} не найдена." });

        return File(data.Value.image, data.Value.contentType);
    }

    /// <summary>
    /// Генерирует и возвращает XLS-файл с распознанными данными.
    /// </summary>
    /// <param name="id">Идентификатор сессии сканирования.</param>
    /// <response code="200">Excel-файл успешно сгенерирован.</response>
    /// <response code="404">Сессия с указанным ID не найдена.</response>
    [HttpGet("{id:guid}/excel")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetExcel(Guid id)
    {
        var dto = _store.GetResult(id);
        if (dto == null)
            return NotFound(new { error = $"Сессия {id} не найдена." });

        using var workbook = new XLWorkbook();

        // ---- Лист 1: ключевые поля ----
        var wsFields = workbook.Worksheets.Add("Поля");
        wsFields.Cell(1, 1).Value = "Поле";
        wsFields.Cell(1, 2).Value = "Значение";
        wsFields.Cell(1, 3).Value = "Уверенность";

        var headerRange = wsFields.Range(1, 1, 1, 3);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 2;
        foreach (var f in dto.Fields)
        {
            wsFields.Cell(row, 1).Value = f.Name;
            wsFields.Cell(row, 2).Value = f.Value;
            wsFields.Cell(row, 3).Value = f.Confidence;
            wsFields.Cell(row, 3).Style.NumberFormat.Format = "0.00%";
            row++;
        }
        wsFields.Columns().AdjustToContents();

        // ---- Лист 2: табличные данные ----
        if (dto.Rows.Count > 0)
        {
            var wsRows = workbook.Worksheets.Add("Таблица");

            var headers = dto.Rows
                .SelectMany(r => r.Cells.Keys)
                .Distinct()
                .ToList();

            wsRows.Cell(1, 1).Value = "#";
            for (int c = 0; c < headers.Count; c++)
                wsRows.Cell(1, c + 2).Value = headers[c];

            var rowsHeader = wsRows.Range(1, 1, 1, headers.Count + 1);
            rowsHeader.Style.Font.Bold = true;
            rowsHeader.Style.Fill.BackgroundColor = XLColor.LightGray;

            int r = 2;
            foreach (var rowData in dto.Rows)
            {
                wsRows.Cell(r, 1).Value = rowData.RowNumber;
                for (int c = 0; c < headers.Count; c++)
                {
                    var key = headers[c];
                    wsRows.Cell(r, c + 2).Value =
                        rowData.Cells.TryGetValue(key, out var v) ? v : "";
                }
                r++;
            }
            wsRows.Columns().AdjustToContents();
        }

        // ---- Лист 3: метаданные ----
        var wsMeta = workbook.Worksheets.Add("Метаданные");
        wsMeta.Cell(1, 1).Value = "Параметр";
        wsMeta.Cell(1, 2).Value = "Значение";
        wsMeta.Range(1, 1, 1, 2).Style.Font.Bold = true;
        wsMeta.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = XLColor.LightGray;

        wsMeta.Cell(2, 1).Value = "ID сессии";
        wsMeta.Cell(2, 2).Value = dto.Id.ToString();
        wsMeta.Cell(3, 1).Value = "Имя файла";
        wsMeta.Cell(3, 2).Value = dto.FileName;
        wsMeta.Cell(4, 1).Value = "MIME-тип";
        wsMeta.Cell(4, 2).Value = dto.ContentType;
        wsMeta.Cell(5, 1).Value = "Время распознавания (UTC)";
        wsMeta.Cell(5, 2).Value = dto.ScannedAt.ToString("yyyy-MM-dd HH:mm:ss");
        wsMeta.Cell(6, 1).Value = "Промпт";
        wsMeta.Cell(6, 2).Value = dto.Prompt ?? "";
        wsMeta.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"{Path.GetFileNameWithoutExtension(dto.FileName)}.xlsx";
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }


    /// <summary>
    /// Тестовый вызов ИИ: проверяет промпт на небольшом изображении,
    /// не сохраняя сессию в хранилище.
    /// </summary>
    /// <param name="file">Тестовое изображение.</param>
    /// <param name="prompt">Промпт для проверки.</param>
    /// <response code="200">Результат тестового распознавания.</response>
    /// <response code="400">Файл не передан или имеет неверный формат.</response>
    /// <response code="500">Ошибка при обращении к сервису ИИ.</response>
    [HttpPost("test-prompt")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(TestPromptResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TestPromptResult>> TestPrompt(
        [FromForm] IFormFile file,
        [FromForm] string? prompt = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не загружен." });

        if (!IsSupportedContentType(file.ContentType))
            return BadRequest(new { error = $"Неподдерживаемый формат: {file.ContentType}." });

        if (string.IsNullOrWhiteSpace(prompt))
            prompt = DefaultPrompt;

        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }

        AiResult recognized;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            recognized = await CallAiServiceAsync(imageBytes, file.ContentType, prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка тестового распознавания");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Ошибка при обращении к ИИ." });
        }
        sw.Stop();

        return Ok(new TestPromptResult
        {
            Prompt = prompt,
            FieldCount = recognized.Fields.Count,
            RowCount = recognized.Rows.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SampleFields = recognized.Fields.Take(5).ToList(),
            SampleRows = recognized.Rows.Take(3).ToList()
        });
    }

    // ============ Вспомогательные методы ============

    private static bool IsSupportedContentType(string contentType) =>
        contentType is "image/jpeg" or "image/jpg" or "image/png"
            or "image/tiff" or "image/bmp";






    private async Task<AiResult> CallAiServiceAsync(
    byte[] imageBytes,
    string contentType,
    string prompt)
    {
        // 1. Создаём HttpClient через фабрику (base address = http://localhost:11434)
        var client = _httpClientFactory.CreateClient("Ollama");

        // 2. Кодируем изображение в base64 — этого требует REST API Ollama
        var base64Image = Convert.ToBase64String(imageBytes);

        // 3. Формируем JSON-схему ответа, чтобы модель вернула строго структурированные данные
        //    (Ollama поддерживает поле "format" для structured outputs) [citation:8]
        var responseSchema = new
        {
            type = "object",
            properties = new
            {
                fields = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string" },
                            value = new { type = "string" },
                            confidence = new { type = "number" }
                        },
                        required = new[] { "name", "value", "confidence" }
                    }
                },
                rows = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            rowNumber = new { type = "integer" },
                            cells = new
                            {
                                type = "object",
                                additionalProperties = new { type = "string" }
                            }
                        },
                        required = new[] { "rowNumber", "cells" }
                    }
                }
            },
            required = new[] { "fields", "rows" }
        };

        // 4. Тело запроса к /api/chat
        //    - model: mistral-small3.1 (vision-модель, требуется Ollama 0.6.5+) [citation:2]
        //    - images: массив base64-строк [citation:1]
        //    - stream: false — ждём полный ответ, а не поток
        //    - format: JSON-схема для детерминированного парсинга
        var requestBody = new
        {
            model = "qwen3.5:9b",
            messages = new[]
            {
            new
            {
                role = "user",
                content = prompt,
                images = new[] { base64Image }
            }
        },
            stream = false,
            format = responseSchema,
            options = new
            {
                temperature = 0.1 // минимальная температура для стабильного распознавания
            }
        };

        // 5. Отправляем POST /api/chat
        var httpContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("api/chat", httpContent);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        // 6. Парсим ответ Ollama
        //    Формат: { "message": { "role": "assistant", "content": "{...json...}" }, ... }
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Ollama вернула пустой ответ.");

        // 7. Десериализуем JSON из поля content в AiResult
        var result = JsonSerializer.Deserialize<AiResult>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result ?? throw new InvalidOperationException("Не удалось разобрать ответ Ollama.");
    }


    #region OpenAI version
    /****************************************************************
    /// <summary>
    /// Вызов сервиса ИИ. Заглушка — замените на реальный вызов
    /// OpenAI Vision / Yandex Vision / Azure AI Document Intelligence.
    /// </summary>
    private async Task<AiResult> CallAiServiceAsync(
        byte[] imageBytes,
        string contentType,
        string prompt)
    {

        // === Пример реального вызова OpenAI Vision ===
        //
        // var client = _httpClientFactory.CreateClient();
        // client.DefaultRequestHeaders.Authorization =
        //     new AuthenticationHeaderValue("Bearer", _config["OpenAI:ApiKey"]);
        //
        // var base64 = Convert.ToBase64String(imageBytes);
        // var payload = new
        // {
        //     model = "gpt-4o",
        //     messages = new object[]
        //     {
        //         new
        //         {
        //             role = "user",
        //             content = new object[]
        //             {
        //                 new { type = "text", text = prompt },
        //                 new { type = "image_url",
        //                       image_url = new { url = $"data:{contentType};base64,{base64}" } }
        //             }
        //         }
        //     },
        //     response_format = new { type = "json_object" }
        // };
        //
        // var httpContent = new StringContent(
        //     JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        //
        // var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", httpContent);
        // response.EnsureSuccessStatusCode();
        //
        // var json = await response.Content.ReadAsStringAsync();
        // using var doc = JsonDocument.Parse(json);
        // var content = doc.RootElement
        //     .GetProperty("choices")[0]
        //     .GetProperty("message")
        //     .GetProperty("content")
        //     .GetString();
        //
        // return JsonSerializer.Deserialize<AiResult>(content!) ?? new AiResult();

        // --- Заглушка для локальной разработки ---
        await Task.Delay(300);

        return new AiResult
        {
            Fields = new List<RecognizedField>
            {
                new() { Name = "Номер документа", Value = "INV-2026-001", Confidence = 0.98 },
                new() { Name = "Дата",           Value = "2026-10-30",   Confidence = 0.95 },
                new() { Name = "Сумма",          Value = "1500.00",     Confidence = 0.91 },
                new() { Name = "Контрагент",     Value = "ООО «Ромашка»", Confidence = 0.87 },
            },
            Rows = new List<RecognizedRow>
            {
                new()
                {
                    RowNumber = 1,
                    Cells = new Dictionary<string, string>
                    {
                        ["Наименование"] = "Услуга А",
                        ["Кол-во"] = "1",
                        ["Цена"] = "1000",
                        ["Итого"] = "1000"
                    }
                },
                new()
                {
                    RowNumber = 2,
                    Cells = new Dictionary<string, string>
                    {
                        ["Наименование"] = "Услуга Б",
                        ["Кол-во"] = "2",
                        ["Цена"] = "250",
                        ["Итого"] = "500"
                    }
                }
            }
        };
    }
    */
    #endregion

    /// <summary>Внутренняя модель ответа ИИ.</summary>
    private class AiResult
    {
        public List<RecognizedField> Fields { get; set; } = new();
        public List<RecognizedRow> Rows { get; set; } = new();
    }
}