using AIMS.Server.Application.DTOs.Psd;

namespace AIMS.Server.Application.Services;

public interface IPsdService
{
    Task<byte[]> CreatePsdFileAsync(PsdRequestDto dto, Action<int, string>? onProgress = null);
}