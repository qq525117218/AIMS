using System.ComponentModel.DataAnnotations;

namespace AIMS.Server.Application.DTOs.Document;

public class WordParseRequestDto
{
    [Required(ErrorMessage = "文件名不能为空")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件的 Base64 字符串
    /// </summary>
    [Required(ErrorMessage = "文件内容不能为空")]
    public string FileContentBase64 { get; set; } = string.Empty;
}