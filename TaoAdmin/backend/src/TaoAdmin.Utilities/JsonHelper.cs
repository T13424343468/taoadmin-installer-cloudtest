using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaoAdmin.Utilities;

/// <summary>
/// JSON 工具：System.Text.Json 统一配置的二次封装（长整型转字符串、忽略空值等）。
/// </summary>
public static class JsonHelper
{
    /// <summary>与网关/前端契约一致的序列化选项</summary>
    public static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.WriteAsString,           // 长整型（雪花ID）转字符串
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>紧凑输出</summary>
    public static readonly JsonSerializerOptions CompactOptions = new() { WriteIndented = false };

    /// <summary>美化输出</summary>
    public static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

    public static string ToJson<T>(T value, JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(value, options ?? WebOptions);

    public static T? FromJson<T>(string json, JsonSerializerOptions? options = null)
        => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, options ?? WebOptions);

    /// <summary>深拷贝：序列化往返</summary>
    public static T DeepClone<T>(T value) where T : class
        => FromJson<T>(ToJson(value)) ?? value;
}
