using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIMS.Server.Infrastructure.Utils;

public static class WestmoonSignUtil
{
    /// <summary>
    /// 生成签名
    /// </summary>
    public static string GenSign<T>(T data, string timestamp, string secret) where T : class
    {
        // 1. 参数判空处理
        if (data == null)
        {
            return ComputeSHA256($"{timestamp}&{secret}");
        }

        // 2. 对象转为有序字典
        var jObj = JObject.FromObject(data);
        var sortedParams = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in jObj.Properties())
        {
            // 忽略 null 值
            if (property.Value.Type == JTokenType.Null) 
            {
                continue; 
            }

            // ✅ 核心修复：移除对 String 的特殊判断
            // 统一使用 ToString(Formatting.None) 确保字符串包含引号 (例如 "SKU123")
            // 这样 code="SKU123" 才能符合 a="a" 的规则
            string valueStr = property.Value.ToString(Formatting.None);

            sortedParams.Add(property.Name, valueStr);
        }

        // 3. 拼接参数 k=v&k=v...
        var sb = new StringBuilder();
        foreach (var item in sortedParams)
        {
            if (sb.Length > 0)
            {
                sb.Append('&');
            }
            sb.Append(item.Key).Append('=').Append(item.Value);
        }

        // 4. 追加尾部参数
        if (sb.Length > 0)
        {
            sb.Append('&');
        }
        sb.Append(timestamp).Append('&').Append(secret);

        // 5. 计算哈希
        // 建议在本地调试时 Console.WriteLine(sb.ToString()) 确认拼接串是否符合预期
        return ComputeSHA256(sb.ToString());
    }

    /// <summary>
    /// 计算 SHA-256 (返回大写 Hex)
    /// </summary>
    private static string ComputeSHA256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes); 
    }
}