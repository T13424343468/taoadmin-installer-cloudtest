using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace TaoAdmin.Utilities;

/// <summary>
/// HttpClient 二次封装：基于 IHttpClientFactory 的链式请求客户端。
/// 用法：builder.Services.AddHttpHelper(); 注入 HttpHelper 后调用 GetAsync/PostAsync。
/// </summary>
public class HttpHelper
{
    private readonly IHttpClientFactory _factory;
    private readonly Dictionary<string, string> _defaultHeaders = new();
    private int _timeoutSeconds = 30;

    public HttpHelper(IHttpClientFactory factory) => _factory = factory;

    /// <summary>绑定指定名称的客户端（可在 AddHttpClient 时自定义代理/证书等）</summary>
    public string ClientName { get; set; } = "default";

    // ---------- 链式配置 ----------

    public HttpHelper Header(string name, string value)
    {
        _defaultHeaders[name] = value;
        return this;
    }

    public HttpHelper Bearer(string token) => Header("Authorization", $"Bearer {token}");

    public HttpHelper Timeout(int seconds)
    {
        _timeoutSeconds = seconds;
        return this;
    }

    // ---------- 请求 ----------

    public async Task<T?> GetAsync<T>(string url, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Get, url, null, ct);

    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
        => await CreateClient().GetStringAsync(url, ct);

    public async Task<T?> PostAsync<T>(string url, object? body = null, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Post, url, body, ct);

    public async Task<T?> PutAsync<T>(string url, object? body = null, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Put, url, body, ct);

    public async Task<T?> DeleteAsync<T>(string url, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Delete, url, null, ct);

    private async Task<T?> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body != null)
        {
            if (body is HttpContent content) request.Content = content;
            else request.Content = JsonContent.Create(body, options: JsonHelper.WebOptions);
        }
        using var client = CreateClient();
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        if (typeof(T) == typeof(string))
            return (T)(object)await response.Content.ReadAsStringAsync(ct);
        return await response.Content.ReadFromJsonAsync<T>(JsonHelper.WebOptions, ct);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
        foreach (var (name, value) in _defaultHeaders)
        {
            client.DefaultRequestHeaders.Remove(name);
            client.DefaultRequestHeaders.Add(name, value);
        }
        return client;
    }
}

/// <summary>HttpHelper 注册扩展</summary>
public static class HttpHelperExtensions
{
    public static IServiceCollection AddHttpHelper(this IServiceCollection services)
    {
        services.AddHttpClient("default");
        services.AddTransient<HttpHelper>();
        return services;
    }
}
