using System.Text;
using SqlSugar;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Application.Services;

/// <summary>
/// SMTP 真实发送实现（替换 NullEmailSender 占位）：
/// 连接配置运行时从 sys_site_config 读取（notify_smtp_* 键），代码不落任何账号密码。
/// 协议：EHLO →（端口 465 隐式 TLS / STARTTLS 协商）→（AUTH LOGIN）→ MAIL FROM → RCPT TO → DATA → QUIT。
/// 连接工厂可注入（测试用脚本流替代真实 TCP/TLS）。
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly ISqlSugarClient _db;
    private readonly Func<string, int, CancellationToken, Task<Stream>>? _connect;
    private readonly Func<Stream, string, Task<Stream>>? _tlsUpgrade;

    public SmtpEmailSender(ISqlSugarClient db,
        Func<string, int, CancellationToken, Task<Stream>>? connect = null,
        Func<Stream, string, Task<Stream>>? tlsUpgrade = null)
    {
        _db = db;
        _connect = connect;
        _tlsUpgrade = tlsUpgrade;
    }

    /// <summary>发送邮件：配置缺失返回失败原因，协议被拒/网络异常返回失败，不抛出。</summary>
    public async Task<(bool Ok, string Message)> SendAsync(string to, string subject, string body)
    {
        var cfg = await LoadConfigAsync();
        if (string.IsNullOrWhiteSpace(cfg.Host))
            return (false, "SMTP 服务器未配置");
        if (string.IsNullOrWhiteSpace(cfg.From))
            return (false, "SMTP 发件人未配置");

        try
        {
            await SendByProtocolAsync(cfg, to, subject, body);
            return (true, "发送成功");
        }
        catch (Exception ex)
        {
            return (false, $"SMTP 发送失败：{ex.Message}");
        }
    }

    internal async Task SendByProtocolAsync(SmtpChannelOptions cfg, string to, string subject, string body)
    {
        var connect = _connect ?? DefaultConnectAsync;
        Stream stream = await connect(cfg.Host, cfg.Port, CancellationToken.None);
        try
        {
            var io = new SmtpIO(stream);

            // 服务端问候语（220）
            SmtpIO.Expect(await io.ReadResponseAsync(), "220");

            await io.SendLineAsync("EHLO taoadmin");
            var ehlo = await io.ReadResponseAsync();
            SmtpIO.Expect(ehlo, "250");

            if (cfg.UseImplicitTls)
            {
                stream = await UpgradeTlsAsync(stream, cfg.Host);
                io = new SmtpIO(stream);
            }
            else if (ehlo.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase))
            {
                await io.CommandAsync("STARTTLS", "220");
                stream = await UpgradeTlsAsync(stream, cfg.Host);
                io = new SmtpIO(stream);
            }

            if (!string.IsNullOrWhiteSpace(cfg.User))
                await AuthLoginAsync(io, cfg.User, cfg.Pass);

            await io.CommandAsync($"MAIL FROM:<{cfg.From}>", "250");
            await io.CommandAsync($"RCPT TO:<{to}>", "250");
            await io.CommandAsync("DATA", "354");
            await io.SendLineAsync(FormatData(cfg.From, to, subject, body));
            SmtpIO.Expect(await io.ReadResponseAsync(), "250");
            await io.CommandAsync("QUIT", "221");
        }
        finally
        {
            stream.Dispose();
        }
    }

    private async Task<SmtpChannelOptions> LoadConfigAsync()
    {
        var rows = await _db.Queryable<SysSiteConfig>()
            .Where(c => c.IsDeleted == false && c.ConfigKey!.StartsWith("notify_smtp_"))
            .ToListAsync();
        string Get(string key) => rows.FirstOrDefault(r => r.ConfigKey == key)?.ConfigValue ?? string.Empty;
        var port = int.TryParse(Get("notify_smtp_port"), out var p) ? p : 25;
        return new SmtpChannelOptions(Get("notify_smtp_host"), port, Get("notify_smtp_user"),
            Get("notify_smtp_pass"), Get("notify_smtp_from"));
    }

    private Task<Stream> UpgradeTlsAsync(Stream inner, string host)
        => _tlsUpgrade != null ? _tlsUpgrade(inner, host) : DefaultTlsUpgradeAsync(inner, host);

    private static async Task<Stream> DefaultTlsUpgradeAsync(Stream inner, string host)
    {
        var ssl = new System.Net.Security.SslStream(inner, false,
            (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(host);
        return ssl;
    }

    private static async Task AuthLoginAsync(SmtpIO io, string user, string pass)
    {
        await io.CommandAsync("AUTH LOGIN", "334");
        await io.CommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(user)), "334");
        await io.CommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(pass)), "235");
    }

    public static string FormatData(string from, string to, string subject, string body)
    {
        var sb = new StringBuilder();
        sb.Append("From: <").Append(from).Append(">\r\n");
        sb.Append("To: <").Append(to).Append(">\r\n");
        sb.Append("Subject: ").Append(EncodeHeader(subject)).Append("\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n\r\n");
        sb.Append(body.Replace("\r\n", "\n").Replace("\n.", "\n..").Replace("\n", "\r\n"));
        sb.Append("\r\n.");
        return sb.ToString();
    }

    private static string EncodeHeader(string value)
    {
        if (value.All(c => c < 128))
            return value;
        return "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
    }

    private static async Task<Stream> DefaultConnectAsync(string host, int port, CancellationToken ct)
    {
        var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(host, port, ct);
        return tcp.GetStream();
    }
}

/// <summary>SMTP 渠道连接参数（运行时从库读取，不落代码）。</summary>
public record SmtpChannelOptions(string Host, int Port, string User, string Pass, string From)
{
    /// <summary>端口 465 走隐式 TLS，其余端口依赖 STARTTLS 协商</summary>
    public bool UseImplicitTls => Port == 465;
}

/// <summary>SMTP 收发明细 IO：按行读写应答，非期望应答码抛异常（含应答原文）。</summary>
public class SmtpIO
{
    private readonly Stream _stream;

    public SmtpIO(Stream stream) => _stream = stream;

    /// <summary>发送一条命令并校验应答码前缀。</summary>
    public async Task CommandAsync(string cmd, string expectCode)
    {
        await SendLineAsync(cmd);
        Expect(await ReadResponseAsync(), expectCode);
    }

    public async Task SendLineAsync(string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
        await _stream.WriteAsync(bytes);
        await _stream.FlushAsync();
    }

    /// <summary>读取一条完整应答：支持多行（"250-" 续行，"250 " 结束）。</summary>
    public async Task<string> ReadResponseAsync()
    {
        var sb = new StringBuilder();
        while (true)
        {
            var line = await ReadLineAsync();
            sb.AppendLine(line);
            if (line.Length < 4 || line[3] == ' ')
                return sb.ToString().TrimEnd();
        }
    }

    private async Task<string> ReadLineAsync()
    {
        var sb = new StringBuilder();
        var read = new byte[1];
        while (true)
        {
            var n = await _stream.ReadAsync(read);
            if (n == 0)
                throw new InvalidOperationException("SMTP 连接被对端关闭");
            if (read[0] == '\n')
                return sb.ToString().TrimEnd('\r');
            sb.Append((char)read[0]);
        }
    }

    public static void Expect(string response, string code)
    {
        if (!response.StartsWith(code))
            throw new InvalidOperationException($"SMTP 应答异常：期望 {code}，实际 {response.TrimEnd()}");
    }
}
