using System.Text.Json.Serialization;

namespace AIMS.Server.Domain.Entities;

public class TokenSession
{
    public string SessionId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserIp { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public DateTime LoginTime { get; set; }
    public DateTime ExpireTime { get; set; }

    /// <summary>
    /// 领域行为：判断会话是否已过期
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.Now > ExpireTime;
    }

    /// <summary>
    /// 领域行为：创建新会话的工厂方法
    /// </summary>
    public static TokenSession Create(string username, string token, string ip, int expireMinutes)
    {
        return new TokenSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Username = username,
            AccessToken = token,
            UserIp = ip,
            LoginTime = DateTime.Now,
            ExpireTime = DateTime.Now.AddMinutes(expireMinutes)
        };
    }
}
