# EF Core 接入方案设计文档

## 1. 需求概述

在 ProCode.Hosting 中接入 Entity Framework Core，建立一套通用的基础核心架构，包含：
- **EF Core 基础配置** - DbContext 配置、数据库连接
- **实体基类体系** - 支持审计、软删除、多租户的实体基类
- **通用仓储层** - 基于 EF Core 的泛型 Repository
- **通用服务层** - CRUD 服务基类
- **自动化特性** - 自动审计时间戳、软删除过滤

## 2. 核心原理

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                     Application Layer                        │
│              (Controllers, Services, DTOs)                   │
├─────────────────────────────────────────────────────────────┤
│                    Domain Services                           │
│         ResourceService<TEntity, TDto, TKey>                 │
├─────────────────────────────────────────────────────────────┤
│                   Repository Layer                           │
│          EfRepository<TEntity, TKey> : IRepository           │
├─────────────────────────────────────────────────────────────┤
│                    EF Core Layer                             │
│    AppDbContext : DbContext (审计拦截, 软删除过滤)            │
├─────────────────────────────────────────────────────────────┤
│                   Entity Base Classes                        │
│   EntityBase → AuditableEntity → FullAuditedEntity           │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 实体继承体系

```
IEntity<TKey>                    # 基础实体接口
    │
    ├── EntityBase<TKey>         # 基础实体 (Id, CreatedAt, UpdatedAt)
    │       │
    │       └── EntityBase       # Guid 主键版本
    │
    ├── ISoftDelete              # 软删除接口 (IsDeleted)
    │
    └── IAuditable               # 审计接口 (CreatedBy, UpdatedBy)
            │
            └── FullAuditedEntity # 完整审计实体 (组合所有特性)
```

### 2.3 自动化机制

1. **SaveChanges 拦截** - 自动填充 CreatedAt/UpdatedAt 时间戳
2. **软删除转换** - Delete 操作自动转为 IsDeleted = true
3. **全局查询过滤** - 自动过滤 IsDeleted = true 的记录
4. **审计字段填充** - 自动填充 CreatedBy/UpdatedBy

## 3. 实现步骤

### 步骤 1: 添加 NuGet 依赖

修改 `ProCode.Hosting/ProCode.Hosting.csproj`：

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0"/>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0"/>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
        <PrivateAssets>all</PrivateAssets>
        <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
</ItemGroup>
```

> 注：初期使用 SQLite 便于开发测试，后续可切换为 PostgreSQL/SQL Server

### 步骤 2: 创建实体基类体系

#### 2.1 创建 `Data/Entities/IEntity.cs`

```csharp
namespace ProCode.Hosting.Data.Entities;

/// <summary>
/// 实体基础接口
/// </summary>
public interface IEntity<TKey> where TKey : IEquatable<TKey>
{
    TKey Id { get; set; }
}

/// <summary>
/// 使用 Guid 主键的实体接口
/// </summary>
public interface IEntity : IEntity<Guid>;
```

#### 2.2 创建 `Data/Entities/IAuditable.cs`

```csharp
namespace ProCode.Hosting.Data.Entities;

/// <summary>
/// 时间审计接口 - 自动记录创建和更新时间
/// </summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 用户审计接口 - 记录操作用户
/// </summary>
public interface IAuditable : IHasTimestamps
{
    string? CreatedBy { get; set; }
    string? UpdatedBy { get; set; }
}
```

#### 2.3 创建 `Data/Entities/ISoftDelete.cs`

```csharp
namespace ProCode.Hosting.Data.Entities;

/// <summary>
/// 软删除接口
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
```

#### 2.4 创建 `Data/Entities/EntityBase.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProCode.Hosting.Data.Entities;

/// <summary>
/// 实体基类 - 提供 Id 和时间戳
/// </summary>
public abstract class EntityBase<TKey> : IEntity<TKey>, IHasTimestamps
    where TKey : IEquatable<TKey>
{
    [Key]
    public TKey Id { get; set; } = default!;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 使用 Guid 主键的实体基类
/// </summary>
public abstract class EntityBase : EntityBase<Guid>, IEntity
{
    protected EntityBase()
    {
        Id = Guid.NewGuid();
    }
}
```

#### 2.5 创建 `Data/Entities/AuditableEntity.cs`

```csharp
namespace ProCode.Hosting.Data.Entities;

/// <summary>
/// 可审计实体 - 包含用户审计信息
/// </summary>
public abstract class AuditableEntity<TKey> : EntityBase<TKey>, IAuditable
    where TKey : IEquatable<TKey>
{
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// 使用 Guid 主键的可审计实体
/// </summary>
public abstract class AuditableEntity : AuditableEntity<Guid>
{
    protected AuditableEntity()
    {
        Id = Guid.NewGuid();
    }
}
```

#### 2.6 创建 `Data/Entities/FullAuditedEntity.cs`

```csharp
namespace ProCode.Hosting.Data.Entities;

/// <summary>
/// 完整审计实体 - 支持软删除 + 用户审计
/// </summary>
public abstract class FullAuditedEntity<TKey> : AuditableEntity<TKey>, ISoftDelete
    where TKey : IEquatable<TKey>
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>
/// 使用 Guid 主键的完整审计实体
/// </summary>
public abstract class FullAuditedEntity : FullAuditedEntity<Guid>
{
    protected FullAuditedEntity()
    {
        Id = Guid.NewGuid();
    }
}
```

### 步骤 3: 创建 DbContext

#### 3.1 创建 `Data/AppDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using ProCode.Hosting.Data.Entities;

namespace ProCode.Hosting.Data;

/// <summary>
/// 应用数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUserService;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUserService currentUserService) : base(options)
    {
        _currentUserService = currentUserService;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 应用所有实体配置
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // 配置软删除全局过滤器
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(GenerateSoftDeleteFilter(entityType.ClrType));
            }
        }
    }

    /// <summary>
    /// 生成软删除过滤表达式
    /// </summary>
    private static LambdaExpression GenerateSoftDeleteFilter(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var property = Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
        var condition = Expression.Equal(property, Expression.Constant(false));
        return Expression.Lambda(condition, parameter);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        OnBeforeSaving();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        OnBeforeSaving();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// 保存前处理 - 自动填充审计字段和处理软删除
    /// </summary>
    private void OnBeforeSaving()
    {
        var now = DateTime.UtcNow;
        var currentUser = _currentUserService?.GetCurrentUserId();

        foreach (var entry in ChangeTracker.Entries())
        {
            // 处理时间戳
            if (entry.Entity is IHasTimestamps timestampEntity)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        timestampEntity.CreatedAt = now;
                        break;
                    case EntityState.Modified:
                        timestampEntity.UpdatedAt = now;
                        break;
                }
            }

            // 处理用户审计
            if (entry.Entity is IAuditable auditableEntity)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditableEntity.CreatedBy = currentUser;
                        break;
                    case EntityState.Modified:
                        auditableEntity.UpdatedBy = currentUser;
                        break;
                }
            }

            // 处理软删除
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDelete softDeleteEntity)
            {
                entry.State = EntityState.Modified;
                softDeleteEntity.IsDeleted = true;
                softDeleteEntity.DeletedAt = now;
                softDeleteEntity.DeletedBy = currentUser;
            }
        }
    }
}
```

#### 3.2 创建 `Data/ICurrentUserService.cs`

```csharp
namespace ProCode.Hosting.Data;

/// <summary>
/// 当前用户服务接口 - 用于获取当前操作用户
/// </summary>
public interface ICurrentUserService
{
    string? GetCurrentUserId();
    string? GetCurrentUserName();
}
```

#### 3.3 创建 `Services/CurrentUserService.cs`

```csharp
using Artisan.Attributes;
using ProCode.Hosting.Data;

namespace ProCode.Hosting.Services;

/// <summary>
/// 当前用户服务实现
/// </summary>
[Service]
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetCurrentUserId()
    {
        return _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("id")?.Value;
    }

    public string? GetCurrentUserName()
    {
        return _httpContextAccessor.HttpContext?.User?.Identity?.Name;
    }
}
```

### 步骤 4: 创建通用仓储层

#### 4.1 创建 `Data/Repositories/IRepository.cs`

```csharp
using System.Linq.Expressions;
using ProCode.Hosting.Data.Entities;
using ProCode.Hosting.Models;

namespace ProCode.Hosting.Data.Repositories;

/// <summary>
/// 通用仓储接口
/// </summary>
public interface IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    // 查询
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<PagedResult<TEntity>> GetPagedAsync(PagedQuery query, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    // 写入
    Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task InsertManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);

    // 查询构建器 (用于复杂查询)
    IQueryable<TEntity> Query(bool includeDeleted = false);
}

/// <summary>
/// 使用 Guid 主键的仓储接口
/// </summary>
public interface IRepository<TEntity> : IRepository<TEntity, Guid>
    where TEntity : class, IEntity<Guid>;
```

#### 4.2 创建 `Data/Repositories/EfRepository.cs`

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProCode.Hosting.Data.Entities;
using ProCode.Hosting.Models;

namespace ProCode.Hosting.Data.Repositories;

/// <summary>
/// EF Core 仓储实现
/// </summary>
public class EfRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    protected readonly AppDbContext DbContext;
    protected readonly DbSet<TEntity> DbSet;

    public EfRepository(AppDbContext dbContext)
    {
        DbContext = dbContext;
        DbSet = dbContext.Set<TEntity>();
    }

    #region 查询操作

    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public virtual async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<List<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(predicate).ToListAsync(cancellationToken);
    }

    public virtual async Task<PagedResult<TEntity>> GetPagedAsync(
        PagedQuery query,
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var queryable = DbSet.AsQueryable();

        if (predicate != null)
        {
            queryable = queryable.Where(predicate);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        // 排序
        if (!string.IsNullOrEmpty(query.SortBy))
        {
            queryable = ApplySort(queryable, query.SortBy, query.SortDescending);
        }
        else
        {
            // 默认按创建时间降序
            if (typeof(IHasTimestamps).IsAssignableFrom(typeof(TEntity)))
            {
                queryable = queryable.OrderByDescending(e => ((IHasTimestamps)e).CreatedAt);
            }
        }

        // 分页
        var items = await queryable
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public virtual async Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(predicate, cancellationToken);
    }

    public virtual async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        return predicate == null
            ? await DbSet.CountAsync(cancellationToken)
            : await DbSet.CountAsync(predicate, cancellationToken);
    }

    public virtual IQueryable<TEntity> Query(bool includeDeleted = false)
    {
        var query = DbSet.AsQueryable();

        if (includeDeleted && typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            query = query.IgnoreQueryFilters();
        }

        return query;
    }

    #endregion

    #region 写入操作

    public virtual async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task InsertManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await DbSet.AddRangeAsync(entities, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Update(entity);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Remove(entity);
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity != null)
        {
            await DeleteAsync(entity, cancellationToken);
        }
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 动态排序
    /// </summary>
    protected virtual IQueryable<TEntity> ApplySort(IQueryable<TEntity> query, string sortBy, bool descending)
    {
        var property = typeof(TEntity).GetProperty(sortBy,
            System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (property == null) return query;

        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var propertyAccess = Expression.MakeMemberAccess(parameter, property);
        var orderByExp = Expression.Lambda(propertyAccess, parameter);

        var methodName = descending ? "OrderByDescending" : "OrderBy";
        var resultExp = Expression.Call(
            typeof(Queryable),
            methodName,
            [typeof(TEntity), property.PropertyType],
            query.Expression,
            Expression.Quote(orderByExp));

        return query.Provider.CreateQuery<TEntity>(resultExp);
    }

    #endregion
}

/// <summary>
/// 使用 Guid 主键的 EF Core 仓储
/// </summary>
public class EfRepository<TEntity> : EfRepository<TEntity, Guid>, IRepository<TEntity>
    where TEntity : class, IEntity<Guid>
{
    public EfRepository(AppDbContext dbContext) : base(dbContext)
    {
    }
}
```

### 步骤 5: 创建通用模型

#### 5.1 创建 `Models/PagedQuery.cs`

```csharp
namespace ProCode.Hosting.Models;

/// <summary>
/// 分页查询参数
/// </summary>
public class PagedQuery
{
    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => value
        };
    }

    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
}
```

#### 5.2 创建 `Models/PagedResult.cs`

```csharp
namespace ProCode.Hosting.Models;

/// <summary>
/// 分页结果
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// 映射到其他类型
    /// </summary>
    public PagedResult<TTarget> Map<TTarget>(Func<T, TTarget> mapper)
    {
        return new PagedResult<TTarget>
        {
            Items = Items.Select(mapper).ToList(),
            TotalCount = TotalCount,
            Page = Page,
            PageSize = PageSize
        };
    }
}
```

#### 5.3 创建 `Models/ApiResponse.cs`

```csharp
namespace ProCode.Hosting.Models;

/// <summary>
/// 统一 API 响应结构
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public object? Errors { get; set; }
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };

    public static ApiResponse<T> Fail(string message, object? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors
    };
}

/// <summary>
/// 无数据的响应
/// </summary>
public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Message = message
    };

    public new static ApiResponse Fail(string message, object? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors
    };
}
```

### 步骤 6: 创建通用服务层

#### 6.1 创建 `Services/IResourceService.cs`

```csharp
using ProCode.Hosting.Data.Entities;
using ProCode.Hosting.Models;

namespace ProCode.Hosting.Services;

/// <summary>
/// 通用资源服务接口
/// </summary>
public interface IResourceService<TEntity, TDto, TKey>
    where TEntity : class, IEntity<TKey>
    where TDto : class
    where TKey : IEquatable<TKey>
{
    Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
    Task<List<TDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<TDto>> GetPagedAsync(PagedQuery query, CancellationToken cancellationToken = default);
    Task<TDto> CreateAsync(TDto dto, CancellationToken cancellationToken = default);
    Task<TDto?> UpdateAsync(TKey id, TDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 使用 Guid 主键的资源服务接口
/// </summary>
public interface IResourceService<TEntity, TDto> : IResourceService<TEntity, TDto, Guid>
    where TEntity : class, IEntity<Guid>
    where TDto : class;
```

#### 6.2 创建 `Services/ResourceServiceBase.cs`

```csharp
using ProCode.Hosting.Data.Entities;
using ProCode.Hosting.Data.Repositories;
using ProCode.Hosting.Models;

namespace ProCode.Hosting.Services;

/// <summary>
/// 通用资源服务基类
/// </summary>
public abstract class ResourceServiceBase<TEntity, TDto, TKey> : IResourceService<TEntity, TDto, TKey>
    where TEntity : class, IEntity<TKey>, new()
    where TDto : class
    where TKey : IEquatable<TKey>
{
    protected readonly IRepository<TEntity, TKey> Repository;

    protected ResourceServiceBase(IRepository<TEntity, TKey> repository)
    {
        Repository = repository;
    }

    /// <summary>
    /// 实体转 DTO (子类必须实现)
    /// </summary>
    protected abstract TDto MapToDto(TEntity entity);

    /// <summary>
    /// DTO 转新实体 (子类必须实现)
    /// </summary>
    protected abstract TEntity MapToEntity(TDto dto);

    /// <summary>
    /// DTO 更新到现有实体 (子类必须实现)
    /// </summary>
    protected abstract void UpdateEntity(TDto dto, TEntity entity);

    public virtual async Task<TDto?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await Repository.GetByIdAsync(id, cancellationToken);
        return entity == null ? null : MapToDto(entity);
    }

    public virtual async Task<List<TDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Repository.GetAllAsync(cancellationToken);
        return entities.Select(MapToDto).ToList();
    }

    public virtual async Task<PagedResult<TDto>> GetPagedAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        var pagedEntities = await Repository.GetPagedAsync(query, null, cancellationToken);
        return pagedEntities.Map(MapToDto);
    }

    public virtual async Task<TDto> CreateAsync(TDto dto, CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(dto);
        var created = await Repository.InsertAsync(entity, cancellationToken);
        return MapToDto(created);
    }

    public virtual async Task<TDto?> UpdateAsync(TKey id, TDto dto, CancellationToken cancellationToken = default)
    {
        var existing = await Repository.GetByIdAsync(id, cancellationToken);
        if (existing == null) return null;

        UpdateEntity(dto, existing);
        var updated = await Repository.UpdateAsync(existing, cancellationToken);
        return MapToDto(updated);
    }

    public virtual async Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await Repository.GetByIdAsync(id, cancellationToken);
        if (entity == null) return false;

        await Repository.DeleteAsync(entity, cancellationToken);
        return true;
    }
}

/// <summary>
/// 使用 Guid 主键的资源服务基类
/// </summary>
public abstract class ResourceServiceBase<TEntity, TDto> : ResourceServiceBase<TEntity, TDto, Guid>
    where TEntity : class, IEntity<Guid>, new()
    where TDto : class
{
    protected ResourceServiceBase(IRepository<TEntity> repository) : base(repository)
    {
    }
}
```

### 步骤 7: 创建 EF Core 模块

#### 7.1 创建 `Modules/EfCoreModule.cs`

```csharp
using Artisan.Attributes;
using Artisan.Modules;
using Microsoft.EntityFrameworkCore;
using ProCode.Hosting.Data;
using ProCode.Hosting.Data.Repositories;

namespace ProCode.Hosting.Modules;

/// <summary>
/// EF Core 模块 - 负责数据库配置和仓储注册
/// </summary>
[Module(Order = -100)] // 优先加载
public class EfCoreModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册 HttpContextAccessor (用于获取当前用户)
        services.AddHttpContextAccessor();

        // 注册 DbContext
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=app.db";

            options.UseSqlite(connectionString);

            // 开发环境启用敏感数据日志
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            if (env.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        // 注册泛型仓储
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IRepository<,>), typeof(EfRepository<,>));
    }

    public override void Configure(WebApplication app)
    {
        // 开发环境自动迁移数据库
        if (app.Environment.IsDevelopment())
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Database.EnsureCreated();
        }
    }
}
```

### 步骤 8: 更新配置文件

#### 8.1 修改 `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=app.db"
  }
}
```

#### 8.2 修改 `appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=app_dev.db"
  }
}
```

## 4. 需要修改、创建或删除的文件

### 需要修改的文件

| 文件路径 | 修改内容 |
|---------|---------|
| `ProCode.Hosting.csproj` | 添加 EF Core NuGet 包引用 |
| `appsettings.json` | 添加连接字符串配置 |
| `appsettings.Development.json` | 添加开发环境连接字符串 |

### 需要创建的文件

| 文件路径 | 说明 |
|---------|------|
| `Data/Entities/IEntity.cs` | 实体接口 |
| `Data/Entities/IAuditable.cs` | 审计接口 |
| `Data/Entities/ISoftDelete.cs` | 软删除接口 |
| `Data/Entities/EntityBase.cs` | 实体基类 |
| `Data/Entities/AuditableEntity.cs` | 可审计实体基类 |
| `Data/Entities/FullAuditedEntity.cs` | 完整审计实体基类 |
| `Data/AppDbContext.cs` | 应用 DbContext |
| `Data/ICurrentUserService.cs` | 当前用户服务接口 |
| `Data/Repositories/IRepository.cs` | 仓储接口 |
| `Data/Repositories/EfRepository.cs` | EF Core 仓储实现 |
| `Models/PagedQuery.cs` | 分页查询参数 |
| `Models/PagedResult.cs` | 分页结果 |
| `Models/ApiResponse.cs` | 统一响应结构 |
| `Services/IResourceService.cs` | 资源服务接口 |
| `Services/ResourceServiceBase.cs` | 资源服务基类 |
| `Services/CurrentUserService.cs` | 当前用户服务实现 |
| `Modules/EfCoreModule.cs` | EF Core 模块 |

### 需要删除的文件

| 文件路径 | 说明 |
|---------|------|
| `docs/feature-restful-resource-module.md` | 之前的设计文档 (已过时) |

## 5. 使用示例

### 5.1 定义实体

```csharp
using ProCode.Hosting.Data.Entities;

public class User : FullAuditedEntity
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
}
```

### 5.2 注册 DbSet

```csharp
// 在 AppDbContext 中添加
public DbSet<User> Users => Set<User>();
```

### 5.3 创建仓储 (可选，使用泛型仓储即可)

```csharp
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username);
}

[Service]
public class UserRepository : EfRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext dbContext) : base(dbContext) { }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await DbSet.FirstOrDefaultAsync(u => u.Username == username);
    }
}
```

### 5.4 创建服务

```csharp
[Service]
public class UserService : ResourceServiceBase<User, UserDto>
{
    public UserService(IRepository<User> repository) : base(repository) { }

    protected override UserDto MapToDto(User entity) => new()
    {
        Id = entity.Id,
        Username = entity.Username,
        Email = entity.Email,
        DisplayName = entity.DisplayName,
        IsActive = entity.IsActive
    };

    protected override User MapToEntity(UserDto dto) => new()
    {
        Username = dto.Username,
        Email = dto.Email,
        DisplayName = dto.DisplayName,
        IsActive = dto.IsActive
    };

    protected override void UpdateEntity(UserDto dto, User entity)
    {
        entity.Username = dto.Username;
        entity.Email = dto.Email;
        entity.DisplayName = dto.DisplayName;
        entity.IsActive = dto.IsActive;
    }
}
```

## 6. 数据库迁移命令

```bash
# 添加迁移
dotnet ef migrations add InitialCreate --project ProCode.Hosting

# 更新数据库
dotnet ef database update --project ProCode.Hosting

# 生成 SQL 脚本 (生产环境)
dotnet ef migrations script --project ProCode.Hosting -o migrations.sql
```

## 7. 扩展性说明

1. **切换数据库** - 修改 `EfCoreModule` 中的 `UseSqlite` 为其他提供程序
2. **添加新实体** - 继承合适的基类，在 `AppDbContext` 添加 `DbSet`
3. **自定义仓储** - 继承 `EfRepository<T>` 添加特定方法
4. **添加拦截器** - 在 `AppDbContext.OnConfiguring` 中添加
5. **多租户支持** - 实现 `ITenantService`，在全局过滤器中添加租户过滤
