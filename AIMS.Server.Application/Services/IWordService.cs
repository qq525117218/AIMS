using AIMS.Server.Application.DTOs.Document;

namespace AIMS.Server.Application.Services;

public interface IWordService
{
    Task<WordParseResponseDto> ParseWordDocumentAsync(WordParseRequestDto request);
}