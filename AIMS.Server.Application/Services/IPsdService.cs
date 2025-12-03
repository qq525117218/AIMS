using AIMS.Server.Application.DTOs.Psd;

namespace AIMS.Server.Application.Services;

public interface IPsdService
{
    Task<byte[]> CreatePsdFileAsync(PsdRequestDto request);
}