using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIMS.Server.Infrastructure.Utils; // 建议放在 Infrastructure/Utils 下

public static class WestmoonSignUtil
{
    /// <summary>
    /// 生成签名
    /// </summary>
    /// <typeparam name="T">请求参数类型</typeparam>
    /// <param name="data">请求参数对象</param>
    /// <param name="timestamp">时间戳</param>
    /// <param name="secret">密钥</param>
    /// <returns>大写的 Hex 签名字符串</returns>
    public static string GenSign<T>(T data, string timestamp, string secret) where T : class
    {
        // 1. 参数判空处理
        if (data == null)
        {
            return ComputeSHA256($"{timestamp}&{secret}");
        }

        // 2. 对象转为有序字典 (SortedDictionary 自动按 Key ASCII 排序)
        // 优化：直接从对象转换，避免了 "对象->JsonString->JObject" 的双重序列化性能损耗
        var jObj = JObject.FromObject(data);
        var sortedParams = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in jObj.Properties())
        {
            // 规范：通常签名算法会忽略 null 值，如果你的业务需要保留 null，去掉这个 if 即可
            if (property.Value.Type == JTokenType.Null) 
            {
                continue; 
            }

            // 格式化：保持 Formatting.None 以去除多余空格，确保与对方系统一致
            string valueStr = property.Value.Type == JTokenType.String 
                ? property.Value.ToString() 
                : property.Value.ToString(Formatting.None);

            sortedParams.Add(property.Name, valueStr);
        }

        // 3. 拼接参数 k=v&k=v...
        // 优化：使用 StringBuilder 比 string.Join 在大量参数时更高效
        var sb = new StringBuilder();
        foreach (var item in sortedParams)
        {
            if (sb.Length > 0)
            {
                sb.Append('&');
            }
            sb.Append(item.Key).Append('=').Append(item.Value);
        }

        // 4. 追加尾部参数 &timestamp&secret
        // 注意：这里完全保留了你原代码的拼接逻辑： paramsStr + "&" + timestamp + "&" + secret
        if (sb.Length > 0)
        {
            sb.Append('&');
        }
        sb.Append(timestamp).Append('&').Append(secret);

        // 5. 计算哈希
        // 调试时可以 Log 出来 sb.ToString() 看看待签名的串对不对
        return ComputeSHA256(sb.ToString());
    }

    /// <summary>
    /// 计算 SHA-256 (返回大写 Hex)
    /// </summary>
    private static string ComputeSHA256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes); // .NET 6/8 推荐写法，比 SHA256.Create() 更快
        return Convert.ToHexString(hashBytes); // .NET 5+ 内置方法，替代 BitConverter + Replace
    }
}