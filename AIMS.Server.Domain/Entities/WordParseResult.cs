namespace AIMS.Server.Domain.Entities;

public class WordParseResult
{
    public string FullText { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public List<string> Paragraphs { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    public List<List<List<string>>> Tables { get; set; } = new();
}