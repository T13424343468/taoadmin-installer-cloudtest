using System.Net;
using System.Net.Sockets;
using System.Text;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// SMTP 真实发送测试：脚本流模拟服务端应答，逐条覆盖协议分支
/// （EHLO/多行应答/STARTTLS/隐式 TLS/AUTH LOGIN/命令被拒/连接中断/配置缺失）。
/// </summary>
public class SmtpEmailSenderTests
{
    /// <summary>脚本流：按队列返回应答，记录客户端写入的每一行命令。</summary>
    private class ScriptedStream : Stream
    {
        private readonly Queue<byte[]> _responses;
        public List<string> Written { get; } = new();
        private byte[] _pending = Array.Empty<byte>();
        private int _pos;

        public ScriptedStream(params string[] responses)
            => _responses = new Queue<byte[]>(responses.Select(r => Encoding.ASCII.GetBytes(r + "\r\n")));

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            Written.Add(Encoding.ASCII.GetString(buffer.Span));
            await Task.CompletedTask;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pos >= _pending.Length)
            {
                if (_responses.Count == 0)
                    return 0; // 对端关闭
                _pending = _responses.Dequeue();
                _pos = 0;
            }
            buffer.Span[0] = _pending[_pos++];
            return 1;
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static (SqlSugarClient Db, ScriptedStream Stream) Create(string[] responses,
        Dictionary<string, string>? config = null)
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig>();
        long configId = 1;
        foreach (var kv in config ?? new Dictionary<string, string>())
        {
            db.Insertable(new SysSiteConfig { Id = configId++, ConfigKey = kv.Key, ConfigValue = kv.Value })
                .ExecuteCommand();
        }
        var stream = new ScriptedStream(responses);
        return (db, stream);
    }

    private static Dictionary<string, string> BaseConfig(int port = 587, string user = "noreply@tao.dev", string pass = "secret")
        => new()
        {
            ["notify_smtp_host"] = "smtp.tao.dev",
            ["notify_smtp_port"] = port.ToString(),
            ["notify_smtp_user"] = user,
            ["notify_smtp_pass"] = pass,
            ["notify_smtp_from"] = "noreply@tao.dev"
        };

    [Fact]
    public async Task SendAsync_WithAuth_NoTls_ShouldSendFullSequence_AndReturnSuccess()
    {
        var (db, stream) = Create(new[]
        {
            "220 smtp ready", "250 hello", "334 VXNlcm5hbWU6", "334 UGFzc3dvcmQ6", "235 ok",
            "250 from ok", "250 to ok", "354 data", "250 accepted", "221 bye"
        }, BaseConfig());
        var sender = new SmtpEmailSender(db, (_, _, _) => Task.FromResult<Stream>(stream));

        var (ok, msg) = await sender.SendAsync("admin@tao.dev", "告警测试", "服务挂了");

        Assert.True(ok, msg);
        Assert.Equal("发送成功", msg);
        Assert.Contains("EHLO taoadmin\r\n", stream.Written);
        Assert.Contains("AUTH LOGIN\r\n", stream.Written);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("noreply@tao.dev")) + "\r\n", stream.Written);
        Assert.Contains("MAIL FROM:<noreply@tao.dev>\r\n", stream.Written);
        Assert.Contains("RCPT TO:<admin@tao.dev>\r\n", stream.Written);
        Assert.Contains("QUIT\r\n", stream.Written);
        // DATA 正文按 ASCII 写入，中文主题以编码字头（Base64）传输
        Assert.Contains(stream.Written,
            w => w.Contains("Subject: =?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("告警测试"))));
    }

    [Fact]
    public async Task SendAsync_NoUser_ShouldSkipAuth()
    {
        var (db, stream) = Create(new[]
        {
            "220 ready", "250 ok", "250 from", "250 to", "354", "250 ok", "221 bye"
        }, BaseConfig(user: "", pass: ""));
        var sender = new SmtpEmailSender(db, (_, _, _) => Task.FromResult<Stream>(stream));

        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.True(ok, msg);
        Assert.DoesNotContain(stream.Written, w => w.StartsWith("AUTH"));
    }

    [Fact]
    public async Task SendAsync_StartTlsAdvertised_ShouldUpgradeBeforeMail()
    {
        var (db, stream) = Create(new[]
        {
            "220 ready", "250-mx.tao.dev\r\n250-STARTTLS\r\n250 SIZE 10485760", "220 go",
            "334 VXNlcm5hbWU6", "334 UGFzc3dvcmQ6", "235 ok",
            "250 from", "250 to", "354", "250 ok", "221 bye"
        }, BaseConfig());
        Stream? upgraded = null;
        var sender = new SmtpEmailSender(db, (_, _, _) => Task.FromResult<Stream>(stream),
            (inner, host) => { upgraded = inner; return Task.FromResult<Stream>(inner); });

        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.True(ok, msg);
        Assert.Same(stream, upgraded);
        Assert.Contains("STARTTLS\r\n", stream.Written);
    }

    [Fact]
    public async Task SendAsync_ImplicitTlsPort465_ShouldUpgradeBeforeEhlo()
    {
        var (db, stream) = Create(new[]
        {
            "220 ready", "250 ok", "250 from", "250 to", "354", "250 ok", "221 bye"
        }, BaseConfig(port: 465, user: "", pass: ""));
        var upgradeCalls = new List<Stream>();
        var sender = new SmtpEmailSender(db, (host, port, _) =>
        {
            Assert.Equal(465, port);
            return Task.FromResult<Stream>(stream);
        }, (inner, _) => { upgradeCalls.Add(inner); return Task.FromResult<Stream>(inner); });

        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.True(ok, msg);
        Assert.Single(upgradeCalls);
        // 隐式 TLS：EHLO 之前先升级，EHLO 是升级后第一条命令
        Assert.Equal("EHLO taoadmin\r\n", stream.Written[0]);
    }

    [Fact]
    public async Task SendAsync_MissingHost_ShouldFailWithoutConnecting()
    {
        var (db, stream) = Create(Array.Empty<string>(), new Dictionary<string, string>
        {
            ["notify_smtp_from"] = "noreply@tao.dev"
        });
        var sender = new SmtpEmailSender(db, (_, _, _) => throw new InvalidOperationException("不应发起连接"));

        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.False(ok);
        Assert.Equal("SMTP 服务器未配置", msg);
        Assert.Empty(stream.Written);
    }

    [Fact]
    public async Task SendAsync_MissingFrom_ShouldFailWithoutConnecting()
    {
        var (db, stream) = Create(Array.Empty<string>(), new Dictionary<string, string>
        {
            ["notify_smtp_host"] = "smtp.tao.dev"
        });
        var sender = new SmtpEmailSender(db, (_, _, _) => throw new InvalidOperationException("不应发起连接"));

        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.False(ok);
        Assert.Equal("SMTP 发件人未配置", msg);
        Assert.Empty(stream.Written);
    }

    [Fact]
    public async Task SendAsync_ServerRejectsCommand_ShouldReturnFailureWithResponse()
    {
        var (db, stream) = Create(new[]
        {
            "220 ready", "250 ok", "550 relay denied"
        }, BaseConfig());
        var sender = new SmtpEmailSender(db, (_, _, _) => Task.FromResult<Stream>(stream));

        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.False(ok);
        Assert.Contains("550", msg);
        Assert.Contains("SMTP 应答异常", msg);
    }

    [Fact]
    public async Task SendAsync_ConnectionClosedMidProtocol_ShouldReturnFailure()
    {
        var (db, stream) = Create(new[] { "220 ready" }, BaseConfig());
        var sender = new SmtpEmailSender(db, (_, _, _) => Task.FromResult<Stream>(stream));

        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.False(ok);
        Assert.Contains("SMTP 连接被对端关闭", msg);
    }

    [Fact]
    public async Task SendAsync_DefaultConnect_ListenerClosed_ShouldReturnFailure()
    {
        // 默认连接工厂走真实 TCP：本地监听后立即关闭，客户端读到 EOF 返回失败
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            client.Close();
            listener.Stop();
        });

        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig>();
        long cfgId = 1;
        foreach (var kv in BaseConfig(port: port, user: "", pass: ""))
            db.Insertable(new SysSiteConfig { Id = cfgId++, ConfigKey = kv.Key, ConfigValue = kv.Value })
                .ExecuteCommand();

        var sender = new SmtpEmailSender(db);
        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.False(ok);
        Assert.StartsWith("SMTP 发送失败", msg);
    }

    [Fact]
    public async Task SendAsync_DefaultTlsUpgrade_HandshakeRejected_ShouldReturnFailure()
    {
        // 未注入 TLS 升级工厂：配置端口 465 走默认 SslStream 升级，
        // 连接工厂自建客户端连接到本机监听，服务端发完问候语即关闭使握手失败（覆盖默认升级分支）
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            // 问候语 + EHLO 应答后即断开：隐式 TLS 升级在 EHLO 之后，握手必然失败
            var script = Encoding.ASCII.GetBytes("220 fake ready\r\n250 fake\r\n");
            await stream.WriteAsync(script);
            await stream.FlushAsync();
            client.Close(); // 不做 TLS 握手直接断开
            listener.Stop();
        });

        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig>();
        long cfgId = 1;
        foreach (var kv in BaseConfig(port: 465, user: "", pass: ""))
            db.Insertable(new SysSiteConfig { Id = cfgId++, ConfigKey = kv.Key, ConfigValue = kv.Value })
                .ExecuteCommand();

        var sender = new SmtpEmailSender(db, async (_, _, _) =>
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            return tcp.GetStream();
        }); // TLS 升级走默认实现
        var (ok, msg) = await sender.SendAsync("a@tao.dev", "s", "b");

        Assert.False(ok);
        Assert.StartsWith("SMTP 发送失败", msg);
    }

    [Fact]
    public void FormatData_AsciiSubject_ShouldKeepPlainHeader_AndStuffDots()
    {
        var data = SmtpEmailSender.FormatData("from@tao.dev", "to@tao.dev", "plain subject", "line1\n.line2\r\nline3");

        Assert.Contains("Subject: plain subject", data);
        Assert.Contains("From: <from@tao.dev>", data);
        Assert.Contains("To: <to@tao.dev>", data);
        Assert.Contains("..line2", data); // 点填充防提前结束 DATA
        Assert.EndsWith("\r\n.", data);
    }

    [Fact]
    public void FormatData_UnicodeSubject_ShouldUseEncodedWord()
    {
        var data = SmtpEmailSender.FormatData("from@tao.dev", "to@tao.dev", "告警", "body");

        Assert.Contains("Subject: =?utf-8?B?", data);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("告警")), data);
    }

    [Fact]
    public void Expect_UnexpectedCode_ShouldThrowWithResponse()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SmtpIO.Expect("550 bad", "250"));
        Assert.Contains("期望 250", ex.Message);
        Assert.Contains("550 bad", ex.Message);
    }

    [Fact]
    public async Task ReadResponseAsync_MultiLine_ShouldJoinUntilSpaceLine()
    {
        var stream = new ScriptedStream("250-first\r\n250-second\r\n250 last");
        var io = new SmtpIO(stream);

        var response = await io.ReadResponseAsync();

        Assert.Contains("250-first", response);
        Assert.Contains("250-second", response);
        Assert.Contains("250 last", response);
    }
}
