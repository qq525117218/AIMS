namespace AIMS.Server.Application.Options;

public class RedisOptions
{
    public const string SectionName = "Redis";
    public string Host { get; set; } = "127.0.0.1";
    public string Port { get; set; } = "6379";
    public string Password { get; set; } = string.Empty;
    public int Database { get; set; } = 0;
    public string Prefix { get; set; } = "AIMS"; 

    public string GetConnectionString()
    {
        return $"{Host}:{Port},password={Password},defaultDatabase={Database},abortConnect=false";
    }
}