using FDBackScan.Models;

namespace FDBackScan.Services;

public interface IAiRecognizer
{
    Task<AiResult> RecognizeAsync(byte[] imageBytes, string contentType, string prompt);
}


