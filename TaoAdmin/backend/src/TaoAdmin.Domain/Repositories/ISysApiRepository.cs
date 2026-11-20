using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Domain.Repositories;

/// <summary>接口资源聚合仓储抽象（写侧）</summary>
public interface ISysApiRepository
{
    Task<bool> ExistsAsync(string path, string httpMethod);
    Task AddAsync(SysApi api);
    Task UpdateAsync(SysApi api);
    Task SoftDeleteAsync(long id);
}
