using Microsoft.Extensions.Options;
using FDBackScan.Configuration;
using FDBackScan.Models;
using System.Text;
using System.Text.Json;

namespace FDBackScan.Services;

public class AiRecognizer : IAiRecognizer
{
    private readonly AiSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;

    public AiRecognizer(IOptions<AiSettings> settings, IHttpClientFactory factory)
    {
        _settings = settings.Value;
        _httpClientFactory = factory;
    }

    public Task<AiResult> RecognizeAsync(byte[] imageBytes, string contentType, string prompt)
    {
        return _settings.Provider switch
        {
            "Ollama" => CallOllamaAsync(imageBytes, contentType, prompt),
            "OpenAI" => CallOpenAiAsync(imageBytes, contentType, prompt),
            "AzureDocumentIntelligence" => CallAzureDocIntelligenceAsync(imageBytes, contentType),
            _ => throw new InvalidOperationException($"Неизвестный провайдер ИИ: {_settings.Provider}")
        };
    }

    // ============ Ollama ============
    private async Task<AiResult> CallOllamaAsync(byte[] imageBytes, string contentType, string prompt)
    {
        var client = _httpClientFactory.CreateClient("Ollama");
        var base64 = Convert.ToBase64String(imageBytes);

        var requestBody = new
        {
            model = _settings.Ollama.Model,
            messages = new[] { new { role = "user", content = prompt, images = new[] { base64 } } },
            stream = false,
            format = BuildJsonSchema(),
            options = new { temperature = _settings.Ollama.Temperature }
        };

        var response = await client.PostAsync("api/chat",
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Ollama вернула пустой ответ.");

        return JsonSerializer.Deserialize<AiResult>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Не удалось разобрать ответ Ollama.");
    }

    // ============ OpenAI ============
    private async Task<AiResult> CallOpenAiAsync(byte[] imageBytes, string contentType, string prompt)
    {
        var client = _httpClientFactory.CreateClient("OpenAi");
        var base64 = Convert.ToBase64String(imageBytes);

        var requestBody = new
        {
            model = _settings.OpenAi.Model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{contentType};base64,{base64}" }
                        }
                    }
                }
            },
            response_format = new { type = "json_object" },
            temperature = _settings.OpenAi.Temperature
        };

        var response = await client.PostAsync("chat/completions",
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("OpenAI вернул пустой ответ.");

        return JsonSerializer.Deserialize<AiResult>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Не удалось разобрать ответ OpenAI.");
    }

    // ============ Azure Document Intelligence ============
    private async Task<AiResult> CallAzureDocIntelligenceAsync(byte[] imageBytes, string contentType)
    {
        var client = _httpClientFactory.CreateClient("AzureDocIntelligence");
        var modelId = _settings.AzureDocIntelligence.ModelId;

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", "document");

        var url = $"/documentintelligence/documentModels/{modelId}:analyze?api-version=2024-11-30";
        var response = await client.PostAsync(url, form);
        response.EnsureSuccessStatusCode();

        // Azure возвращает 202 с Operation-Location; для простоты используем polling
        var operationLocation = response.Headers.GetValues("Operation-Location").FirstOrDefault();
        if (string.IsNullOrEmpty(operationLocation))
            throw new InvalidOperationException("Azure не вернул Operation-Location.");

        // Polling (упрощённо — в продакшене вынесите в отдельный цикл с задержкой)
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            var pollResponse = await client.GetAsync(operationLocation);
            var pollJson = await pollResponse.Content.ReadAsStringAsync();
            using var pollDoc = JsonDocument.Parse(pollJson);
            var status = pollDoc.RootElement.GetProperty("status").GetString();

            if (status == "succeeded")
            {
                return MapAzureResult(pollDoc.RootElement.GetProperty("analyzeResult"));
            }
            if (status == "failed")
                throw new InvalidOperationException("Azure Document Intelligence вернул failed.");
        }

        throw new TimeoutException("Azure Document Intelligence не завершил анализ вовремя.");
    }

    private static AiResult MapAzureResult(JsonElement analyzeResult)
    {
        var result = new AiResult();

        // Azure возвращает documents[0].fields — маппим в RecognizedField
        if (analyzeResult.TryGetProperty("documents", out var documents) &&
            documents.GetArrayLength() > 0)
        {
            var doc = documents[0];
            if (doc.TryGetProperty("fields", out var fields))
            {
                foreach (var field in fields.EnumerateObject())
                {
                    var value = field.Value;
                    string? strValue = value.TryGetProperty("content", out var c) ? c.GetString() : null;
                    double confidence = value.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0;

                    if (!string.IsNullOrEmpty(strValue))
                    {
                        result.Fields.Add(new RecognizedField
                        {
                            Name = field.Name,
                            Value = strValue,
                            Confidence = confidence
                        });
                    }
                }
            }
        }

        // Табличные данные из pages.tables (упрощённо)
        if (analyzeResult.TryGetProperty("tables", out var tables))
        {
            int rowNum = 1;
            foreach (var table in tables.EnumerateArray())
            {
                if (!table.TryGetProperty("cells", out var cells)) continue;
                foreach (var cell in cells.EnumerateArray())
                {
                    var rowIndex = cell.TryGetProperty("rowIndex", out var ri) ? ri.GetInt32() : 0;
                    var colIndex = cell.TryGetProperty("columnIndex", out var ci) ? ci.GetInt32() : 0;
                    var content = cell.TryGetProperty("content", out var cc) ? cc.GetString() : "";

                    if (rowIndex == 0) continue; // пропускаем заголовок

                    // Для простоты создаём строки по rowIndex
                    while (result.Rows.Count <= rowIndex - 1)
                    {
                        result.Rows.Add(new RecognizedRow
                        {
                            RowNumber = result.Rows.Count + 1,
                            Cells = new Dictionary<string, string>()
                        });
                    }
                    result.Rows[rowIndex - 1].Cells[$"col{colIndex}"] = content ?? "";
                }
            }
        }

        return result;
    }

    private static object BuildJsonSchema() => new
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
                        cells = new { type = "object", additionalProperties = new { type = "string" } }
                    },
                    required = new[] { "rowNumber", "cells" }
                }
            }
        },
        required = new[] { "fields", "rows" }
    };
}
