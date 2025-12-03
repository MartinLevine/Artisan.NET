# Artisan.NET AOP 框架设计

> **注意：** 此功能为后续迭代计划，当前版本暂不实现。

## 1. 设计理念

**基于 DispatchProxy 的轻量级 AOP**

通过 Attribute 标记实现声明式的横切关注点，如日志、缓存、重试、事务等。

```csharp
[Service]
[Log]  // 类级别：所有方法都会被记录
public class OrderService : IOrderService
{
    [Cache(DurationSeconds = 300)]  // 方法级别缓存
    public Order GetOrder(int id) { ... }

    [Transaction]  // 事务
    [Retry(MaxRetries = 3)]  // 重试
    public void CreateOrder(Order order) { ... }
}
```

## 2. 核心接口与 Attributes

```csharp
namespace Artisan.Aop;

/// <summary>
/// 拦截器接口
/// </summary>
public interface IInterceptor
{
    Task<object?> InterceptAsync(IInvocation invocation, Func<Task<object?>> next);
}

/// <summary>
/// 调用上下文
/// </summary>
public interface IInvocation
{
    object Target { get; }
    MethodInfo Method { get; }
    object?[] Arguments { get; }
    Type ReturnType { get; }
}

/// <summary>
/// 拦截器 Attribute
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class InterceptAttribute : Attribute
{
    public Type InterceptorType { get; }
    public int Order { get; set; } = 0;

    public InterceptAttribute(Type interceptorType)
    {
        if (!typeof(IInterceptor).IsAssignableFrom(interceptorType))
            throw new ArgumentException("Type must implement IInterceptor");
        InterceptorType = interceptorType;
    }
}

/// <summary>
/// 内置拦截器 Attributes
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class LogAttribute : InterceptAttribute
{
    public LogAttribute() : base(typeof(LogInterceptor)) { }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CacheAttribute : InterceptAttribute
{
    public int DurationSeconds { get; set; } = 60;
    public CacheAttribute() : base(typeof(CacheInterceptor)) { }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RetryAttribute : InterceptAttribute
{
    public int MaxRetries { get; set; } = 3;
    public int DelayMs { get; set; } = 1000;
    public RetryAttribute() : base(typeof(RetryInterceptor)) { }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class TransactionAttribute : InterceptAttribute
{
    public TransactionAttribute() : base(typeof(TransactionInterceptor)) { }
}
```

## 3. 内置拦截器实现

### 3.1 LogInterceptor - 日志拦截器

```csharp
public class LogInterceptor : IInterceptor
{
    private readonly ILogger<LogInterceptor> _logger;

    public LogInterceptor(ILogger<LogInterceptor> logger)
    {
        _logger = logger;
    }

    public async Task<object?> InterceptAsync(IInvocation invocation, Func<Task<object?>> next)
    {
        var methodName = $"{invocation.Target.GetType().Name}.{invocation.Method.Name}";
        var args = string.Join(", ", invocation.Arguments.Select(a => a?.ToString() ?? "null"));

        _logger.LogInformation("Entering {Method}({Args})", methodName, args);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await next();
            stopwatch.Stop();
            _logger.LogInformation("Exiting {Method} - Duration: {Duration}ms, Result: {Result}",
                methodName, stopwatch.ElapsedMilliseconds, result);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Exception in {Method} - Duration: {Duration}ms",
                methodName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
```

### 3.2 CacheInterceptor - 缓存拦截器

```csharp
public class CacheInterceptor : IInterceptor
{
    private readonly IMemoryCache _cache;

    public CacheInterceptor(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<object?> InterceptAsync(IInvocation invocation, Func<Task<object?>> next)
    {
        // 只缓存有返回值的方法
        if (invocation.ReturnType == typeof(void) || invocation.ReturnType == typeof(Task))
        {
            return await next();
        }

        // 获取 CacheAttribute 配置
        var cacheAttr = invocation.Method.GetCustomAttribute<CacheAttribute>();
        var duration = cacheAttr?.DurationSeconds ?? 60;

        // 生成缓存 Key
        var cacheKey = GenerateCacheKey(invocation);

        if (_cache.TryGetValue(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        var result = await next();

        if (result != null)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(duration));
        }

        return result;
    }

    private string GenerateCacheKey(IInvocation invocation)
    {
        var typeName = invocation.Target.GetType().FullName;
        var methodName = invocation.Method.Name;
        var argsHash = string.Join("_", invocation.Arguments.Select(a => a?.GetHashCode() ?? 0));
        return $"{typeName}.{methodName}:{argsHash}";
    }
}
```

### 3.3 RetryInterceptor - 重试拦截器

```csharp
public class RetryInterceptor : IInterceptor
{
    private readonly ILogger<RetryInterceptor> _logger;

    public RetryInterceptor(ILogger<RetryInterceptor> logger)
    {
        _logger = logger;
    }

    public async Task<object?> InterceptAsync(IInvocation invocation, Func<Task<object?>> next)
    {
        var retryAttr = invocation.Method.GetCustomAttribute<RetryAttribute>()
            ?? invocation.Target.GetType().GetCustomAttribute<RetryAttribute>();

        var maxRetries = retryAttr?.MaxRetries ?? 3;
        var delayMs = retryAttr?.DelayMs ?? 1000;

        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await next();
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed for {Method}",
                    attempt, maxRetries, invocation.Method.Name);

                if (attempt < maxRetries)
                {
                    await Task.Delay(delayMs * attempt);  // 指数退避
                }
            }
        }

        throw new AggregateException($"All {maxRetries} retry attempts failed", lastException!);
    }
}
```

### 3.4 TransactionInterceptor - 事务拦截器

```csharp
public class TransactionInterceptor : IInterceptor
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public TransactionInterceptor(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<object?> InterceptAsync(IInvocation invocation, Func<Task<object?>> next)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var result = await next();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

## 4. 自定义拦截器示例

```csharp
// 权限验证拦截器
public class AuthorizeAttribute : InterceptAttribute
{
    public string[] Roles { get; set; } = Array.Empty<string>();
    public AuthorizeAttribute() : base(typeof(AuthorizeInterceptor)) { }
}

public class AuthorizeInterceptor : IInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthorizeInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<object?> InterceptAsync(IInvocation invocation, Func<Task<object?>> next)
    {
        var authAttr = invocation.Method.GetCustomAttribute<AuthorizeAttribute>()
            ?? invocation.Target.GetType().GetCustomAttribute<AuthorizeAttribute>();

        var user = _httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("User is not authenticated");
        }

        if (authAttr?.Roles.Length > 0)
        {
            var hasRole = authAttr.Roles.Any(role => user.IsInRole(role));
            if (!hasRole)
            {
                throw new UnauthorizedAccessException($"User does not have required roles: {string.Join(", ", authAttr.Roles)}");
            }
        }

        return await next();
    }
}

// 使用
[Service]
public class AdminService : IAdminService
{
    [Authorize(Roles = new[] { "Admin", "SuperAdmin" })]
    public void DeleteUser(int userId) { ... }
}
```

## 5. AOP 实现核心（基于 DispatchProxy）

```csharp
internal class InterceptorProxy<T> : DispatchProxy where T : class
{
    private T _target = null!;
    private IServiceProvider _serviceProvider = null!;
    private IInterceptor[] _classInterceptors = null!;

    public static T Create(T target, IServiceProvider serviceProvider)
    {
        var proxy = Create<T, InterceptorProxy<T>>() as InterceptorProxy<T>;
        proxy!._target = target;
        proxy._serviceProvider = serviceProvider;
        proxy._classInterceptors = GetClassInterceptors(typeof(T), serviceProvider);
        return (proxy as T)!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null) return null;

        var methodInterceptors = GetMethodInterceptors(targetMethod, _serviceProvider);
        var allInterceptors = _classInterceptors.Concat(methodInterceptors)
            .OrderBy(i => i.GetType().GetCustomAttribute<InterceptAttribute>()?.Order ?? 0)
            .ToArray();

        if (allInterceptors.Length == 0)
            return targetMethod.Invoke(_target, args);

        var invocation = new Invocation(_target, targetMethod, args ?? Array.Empty<object?>());
        return ExecuteInterceptorChain(invocation, allInterceptors, 0).GetAwaiter().GetResult();
    }

    private async Task<object?> ExecuteInterceptorChain(
        IInvocation invocation, IInterceptor[] interceptors, int index)
    {
        if (index >= interceptors.Length)
        {
            return invocation.Method.Invoke(invocation.Target, invocation.Arguments);
        }

        return await interceptors[index].InterceptAsync(
            invocation,
            () => ExecuteInterceptorChain(invocation, interceptors, index + 1));
    }

    private static IInterceptor[] GetClassInterceptors(Type type, IServiceProvider sp)
    {
        return type.GetCustomAttributes<InterceptAttribute>()
            .OrderBy(a => a.Order)
            .Select(a => (IInterceptor)sp.GetRequiredService(a.InterceptorType))
            .ToArray();
    }

    private static IInterceptor[] GetMethodInterceptors(MethodInfo method, IServiceProvider sp)
    {
        return method.GetCustomAttributes<InterceptAttribute>()
            .OrderBy(a => a.Order)
            .Select(a => (IInterceptor)sp.GetRequiredService(a.InterceptorType))
            .ToArray();
    }
}

/// <summary>
/// 调用上下文实现
/// </summary>
internal class Invocation : IInvocation
{
    public object Target { get; }
    public MethodInfo Method { get; }
    public object?[] Arguments { get; }
    public Type ReturnType => Method.ReturnType;

    public Invocation(object target, MethodInfo method, object?[] arguments)
    {
        Target = target;
        Method = method;
        Arguments = arguments;
    }
}
```

## 6. 注意事项与限制

| 限制 | 说明 | 解决方案 |
|------|------|----------|
| **必须通过接口注入** | DispatchProxy 只能代理接口 | 服务必须定义接口并通过接口注入 |
| **密封类不可代理** | 无法继承密封类 | 避免在需要 AOP 的服务上使用 sealed |
| **异步方法处理** | 需要正确处理 Task 返回值 | 拦截器统一使用 async/await |

## 7. 文件结构

```
Artisan/
└── src/
    └── Artisan/
        └── Aop/
            ├── IInterceptor.cs
            ├── IInvocation.cs
            ├── InterceptAttribute.cs
            ├── InterceptorProxy.cs
            └── Interceptors/
                ├── LogInterceptor.cs
                ├── CacheInterceptor.cs
                ├── RetryInterceptor.cs
                └── TransactionInterceptor.cs
```

---

**此功能待后续迭代实现。**
