using FDBackScan.Models;
using System.Collections.Concurrent;

namespace FDBackScan.Services
{
    public class InMemoryScanStore : IScanStore
    {
        private readonly ConcurrentDictionary<Guid, (byte[] image, string contentType, ScanResultDto result)> _store = new();

        public void Save(Guid id, byte[] image, string contentType, ScanResultDto result)
            => _store[id] = (image, contentType, result);

        public (byte[] image, string contentType)? GetImage(Guid id)
            => _store.TryGetValue(id, out var v) ? (v.image, v.contentType) : null;

        public ScanResultDto? GetResult(Guid id)
            => _store.TryGetValue(id, out var v) ? v.result : null;
    }
}
