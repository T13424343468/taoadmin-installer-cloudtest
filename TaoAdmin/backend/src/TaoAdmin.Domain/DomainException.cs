namespace TaoAdmin.Domain;

/// <summary>领域层业务异常：由宿主的异常中间件统一转为 ApiResult 返回</summary>
public class DomainException : Exception
{
    /// <summary>业务错误码（HTTP 语义：400 参数/业务失败，403 无权限，404 不存在）</summary>
    public int Code { get; }

    public DomainException(string message, int code = 400) : base(message) => Code = code;
}
