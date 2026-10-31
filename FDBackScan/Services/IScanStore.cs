using FDBackScan.Models;

namespace FDBackScan.Services
{
    public interface IScanStore
    {
        void Save(Guid id, byte[] image, string contentType, ScanResultDto result);
        (byte[] image, string contentType)? GetImage(Guid id);
        ScanResultDto? GetResult(Guid id);
    }
}
