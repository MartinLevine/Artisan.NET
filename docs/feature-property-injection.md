# Artisan.NET 属性注入功能设计

## 1. 问题分析

### 1.1 当前状态

经过测试发现，Artisan 框架存在**两个独立的问题**：

1. **GlobMatcher 模式匹配 Bug** - 导致服务扫描失败，构造函数注入也不工作
2. **PropertyInjector 未被调用** - 导致属性注入不工作

### 1.2 问题一：GlobMatcher 模式匹配失败

**测试日志**：
```
Pattern: ProCode.Hosting.**
Name:    ProCode.Hosting
Result:  IsMatch = False  ❌ (应该是 True)
```

**根本原因**：

自定义的 `GlobMatcher` 实现有 bug。`**` 模式转换成的正则表达式要求必须有后续内容：

```
Pattern:  ProCode.Hosting.**
Regex:    ^ProCode\.Hosting\..*$
          ↑ 要求必须以 "ProCode.Hosting." 开头且后面有内容

Name:     ProCode.Hosting
          ↑ 没有尾部的 ".xxx"，匹配失败！
```

**影响范围**：
- `[Service]`、`[Repository]`、`[Component]` 标记的类无法被扫描到
- 服务无法注册到 DI 容器
- 构造函数注入也失效（因为服务根本没注册）

**解决方案**：使用成熟的 NuGet 包 [DotNet.Glob](https://www.nuget.org/packages/DotNet.Glob) 替代自定义实现。

### 1.3 问题二：PropertyInjector 未被调用

即使服务被正确注册，`PropertyInjector` 也从未被调用：

```
┌─────────────────────────────────────────────────────────────┐
│                    对象创建流程                              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Controller 创建:                                           │
│  ┌──────────────────┐    ┌──────────────────┐              │
│  │ControllerFactory │───>│ControllerActivator│──> Controller│
│  └──────────────────┘    └──────────────────┘              │
│         ↓                                                   │
│  默认使用 DefaultControllerActivator                        │
│  只支持构造函数注入，不调用 PropertyInjector                 │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  普通服务创建:                                               │
│  ┌──────────────────┐                                       │
│  │  ServiceProvider │──────────────────────────> Service    │
│  └──────────────────┘                                       │
│         ↓                                                   │
│  使用 ServiceDescriptor 中的工厂方法或直接构造               │
│  不调用 PropertyInjector                                    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 1.4 需要解决的场景

| 场景 | 对象类型 | 创建方式 | 需要的方案 |
|-----|---------|---------|-----------|
| 0 | 所有服务 | AssemblyScanner | 修复 GlobMatcher |
| 1 | Controller | ControllerActivator | 自定义 ControllerActivator |
| 2 | Service/Repository/Component | ServiceProvider | 工厂包装 |
| 3 | 用户手动 new 的对象 | 手动创建 | 提供 API 手动调用 |

## 2. 设计方案

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                    修复后的架构                              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │              AssemblyScanner (修复)                  │   │
│  │  使用 DotNet.Glob 替代自定义 GlobMatcher             │   │
│  └─────────────────────────────────────────────────────┘   │
│                          │                                  │
│                          ▼ 扫描到的类型                     │
│  ┌─────────────────────────────────────────────────────┐   │
│  │              ServiceRegistrar (增强)                 │   │
│  │  - 注册服务到 DI 容器                                │   │
│  │  - 使用工厂包装支持属性注入                          │   │
│  └─────────────────────────────────────────────────────┘   │
│                          │                                  │
│                          ▼                                  │
│  ┌─────────────────────────────────────────────────────┐   │
│  │                  IPropertyInjector                   │   │
│  │  - InjectProperties(object instance)                │   │
│  │  - InjectPropertiesAsync(object instance)           │   │
│  └─────────────────────────────────────────────────────┘   │
│                          ▲                                  │
│                          │ 调用                             │
│          ┌───────────────┼───────────────┐                 │
│          │               │               │                 │
│  ┌───────┴───────┐ ┌─────┴─────┐ ┌───────┴───────┐        │
│  │ArtisanController│ │ServiceFactory│ │手动调用 API │        │
│  │   Activator    │ │  Wrapper    │ │             │        │
│  └───────────────┘ └───────────┘ └───────────────┘        │
│          │               │               │                 │
│          ▼               ▼               ▼                 │
│     Controller       Service         任意对象              │
│                    Repository                              │
│                    Component                               │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 问题一解决：替换 GlobMatcher

#### 2.2.1 引入 Microsoft.Extensions.FileSystemGlobbing

**添加 NuGet 引用**：
```xml
<PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="10.0.0" />
```

**Microsoft.Extensions.FileSystemGlobbing 优势**：
- Microsoft 官方库，与 .NET 生态紧密集成
- 支持标准 Glob 语法，包括 `**` 匹配零个或多个字符
- 性能优良，广泛应用于 .NET 工具链

#### 2.2.2 修改 GlobMatcher

```csharp
using Microsoft.Extensions.FileSystemGlobbing;

namespace Artisan.DependencyInjection;

/// <summary>
/// Glob 模式匹配器
/// 使用 Microsoft.Extensions.FileSystemGlobbing 库实现
/// </summary>
public static class GlobMatcher
{
    /// <summary>
    /// 检查名称是否匹配 Glob 模式
    /// </summary>
    /// <param name="pattern">Glob 模式</param>
    /// <param name="name">要匹配的名称</param>
    /// <returns>是否匹配</returns>
    /// <remarks>
    /// 支持的通配符：
    /// *     - 匹配任意字符（不含分隔符）
    /// **    - 匹配任意字符（含分隔符），可匹配零个或多个段
    /// ?     - 匹配单个字符
    /// [abc] - 匹配字符集
    /// </remarks>
    public static bool IsMatch(string pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(name))
            return false;

        try
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern);

            // FileSystemGlobbing 针对路径设计，需要用虚拟路径匹配
            // 将程序集名称转换为路径格式进行匹配
            var pathName = name.Replace(".", "/");
            return matcher.Match(pathName).HasMatches;
        }
        catch
        {
            return false;
        }
    }
}
```

#### 2.2.3 修改 AssemblyScanner

修改扫描逻辑，确保程序集名称本身也能被匹配：

```csharp
/// <summary>
/// 检查程序集是否应该被扫描
/// </summary>
private bool ShouldScanAssembly(string? assemblyName, List<string> patterns)
{
    if (string.IsNullOrEmpty(assemblyName))
        return false;

    foreach (var pattern in patterns)
    {
        // 直接匹配程序集名
        if (GlobMatcher.IsMatch(pattern, assemblyName))
            return true;

        // Microsoft.Extensions.FileSystemGlobbing 的 ** 可以匹配零个或多个字符，所以自动工作
    }

    return false;
}
```

### 2.3 问题二解决：属性注入集成

#### 2.3.1 ArtisanControllerActivator

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.AspNetCore;

/// <summary>
/// Artisan Controller 激活器
/// 在 Controller 创建后自动调用属性注入
/// </summary>
public class ArtisanControllerActivator : IControllerActivator
{
    public object Create(ControllerContext context)
    {
        var controllerType = context.ActionDescriptor.ControllerTypeInfo.AsType();

        // 使用 ActivatorUtilities 创建 Controller（支持构造函数注入）
        var controller = ActivatorUtilities.CreateInstance(
            context.HttpContext.RequestServices,
            controllerType);

        // 获取属性注入器并注入
        var injector = context.HttpContext.RequestServices
            .GetService<IPropertyInjector>();
        injector?.InjectProperties(controller);

        return controller;
    }

    public void Release(ControllerContext context, object controller)
    {
        if (controller is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public ValueTask ReleaseAsync(ControllerContext context, object controller)
    {
        if (controller is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        Release(context, controller);
        return ValueTask.CompletedTask;
    }
}
```

#### 2.3.2 ServiceRegistrar 改造

```csharp
/// <summary>
/// 注册可注入类型（带属性注入支持）
/// </summary>
private static void RegisterInjectableType(
    IServiceCollection services,
    Type implementationType,
    InjectableAttribute attribute)
{
    var lifetime = ConvertLifetime(attribute.Lifetime);
    var interfaces = GetServiceInterfaces(implementationType);

    // 检查是否需要属性注入
    var needsPropertyInjection = NeedsPropertyInjection(implementationType);

    if (needsPropertyInjection)
    {
        // 使用工厂包装，创建后进行属性注入
        RegisterWithPropertyInjection(services, implementationType, interfaces, lifetime, attribute.Key);
    }
    else
    {
        // 无需属性注入，使用标准注册
        RegisterStandard(services, implementationType, interfaces, lifetime, attribute.Key);
    }
}

/// <summary>
/// 检查类型是否需要属性注入
/// </summary>
private static bool NeedsPropertyInjection(Type type)
{
    var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    // 检查是否有 [Inject] 标记的属性或字段
    var hasInject = type.GetProperties(bindingFlags)
        .Any(p => p.GetCustomAttribute<InjectAttribute>() != null)
        || type.GetFields(bindingFlags)
        .Any(f => f.GetCustomAttribute<InjectAttribute>() != null);

    // 检查是否有 [AppSetting] 标记的属性
    var hasAppSetting = type.GetProperties(bindingFlags)
        .Any(p => p.GetCustomAttribute<AppSettingAttribute>() != null);

    // 检查是否有 [GetValue] 标记的属性或字段
    var hasGetValue = type.GetProperties(bindingFlags)
        .Any(p => p.GetCustomAttribute<GetValueAttribute>() != null)
        || type.GetFields(bindingFlags)
        .Any(f => f.GetCustomAttribute<GetValueAttribute>() != null);

    return hasInject || hasAppSetting || hasGetValue;
}

/// <summary>
/// 使用属性注入工厂注册服务
/// </summary>
private static void RegisterWithPropertyInjection(
    IServiceCollection services,
    Type implementationType,
    List<Type> interfaces,
    ServiceLifetime lifetime,
    string? key)
{
    // 创建工厂方法
    object Factory(IServiceProvider sp)
    {
        var instance = ActivatorUtilities.CreateInstance(sp, implementationType);
        var injector = sp.GetRequiredService<IPropertyInjector>();
        injector.InjectProperties(instance);
        return instance;
    }

    if (key != null)
    {
        // Keyed 服务注册
        foreach (var iface in interfaces)
        {
            services.Add(new ServiceDescriptor(iface, key, (sp, _) => Factory(sp), lifetime));
        }
        if (!interfaces.Any())
        {
            services.Add(new ServiceDescriptor(implementationType, key, (sp, _) => Factory(sp), lifetime));
        }
    }
    else
    {
        // 普通服务注册
        foreach (var iface in interfaces)
        {
            services.Add(new ServiceDescriptor(iface, Factory, lifetime));
        }
        // 同时注册实现类本身
        services.Add(new ServiceDescriptor(implementationType, Factory, lifetime));
    }
}
```

#### 2.3.3 ArtisanApplication 集成

在 `ArtisanApplication.RunAsync` 中自动注册属性注入支持：

```csharp
// 阶段 2：服务注册
// 注册属性注入器
builder.Services.AddPropertyInjector();

// 替换 Controller 激活器以支持属性注入
builder.Services.AddSingleton<IControllerActivator, ArtisanControllerActivator>();
```

## 3. 实现步骤

### 步骤 1：添加 Microsoft.Extensions.FileSystemGlobbing 依赖

**文件**：`Artisan/Artisan.csproj`

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="10.0.0" />
</ItemGroup>
```

### 步骤 2：重写 GlobMatcher

**文件**：`Artisan/DependencyInjection/GlobMatcher.cs`

使用 DotNet.Glob 替代自定义实现。

### 步骤 3：创建 ArtisanControllerActivator

**文件**：`Artisan/AspNetCore/ArtisanControllerActivator.cs`

### 步骤 4：修改 ServiceRegistrar

**文件**：`Artisan/DependencyInjection/ServiceRegistrar.cs`

添加工厂包装逻辑，使服务在创建后自动进行属性注入。

### 步骤 5：修改 ArtisanApplication

**文件**：`Artisan/Application/ArtisanApplication.cs`

在服务注册阶段自动添加属性注入支持。

## 4. 文件变更清单

| 操作 | 文件路径 | 说明 |
|-----|---------|------|
| 修改 | `Artisan/Artisan.csproj` | 添加 DotNet.Glob 依赖 |
| 重写 | `Artisan/DependencyInjection/GlobMatcher.cs` | 使用 DotNet.Glob 实现 |
| 新增 | `Artisan/AspNetCore/ArtisanControllerActivator.cs` | Controller 属性注入激活器 |
| 修改 | `Artisan/DependencyInjection/ServiceRegistrar.cs` | 添加工厂包装逻辑 |
| 修改 | `Artisan/DependencyInjection/PropertyInjector.cs` | 确保正确注册 |
| 修改 | `Artisan/Application/ArtisanApplication.cs` | 集成属性注入 |

## 5. 使用示例

### 5.1 Controller 中使用

```csharp
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    // 属性注入
    [Inject]
    public IUserService UserService { get; set; } = null!;

    // 配置类注入
    [AppSetting("App")]
    public AppConfig? AppConfig { get; set; }

    // 配置值注入
    [GetValue("App:Name")]
    public string AppName { get; set; } = "";

    // 构造函数注入仍然可用
    private readonly ILogger<UserController> _logger;

    public UserController(ILogger<UserController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        _logger.LogInformation("Getting users from {AppName}", AppName);
        return Ok(UserService.GetAllUsers());
    }
}
```

### 5.2 Service 中使用

```csharp
[Service]
public class UserService : IUserService
{
    // 属性注入
    [Inject]
    private IUserRepository Repository { get; set; } = null!;

    // Keyed 服务注入
    [Inject("redis")]
    private ICache? Cache { get; set; }

    // 配置值注入
    [GetValue("App:Name")]
    private string AppName { get; set; } = "";

    public IEnumerable<User> GetAllUsers()
    {
        Console.WriteLine($"[{AppName}] Getting all users");
        return Repository.GetAll();
    }
}
```

## 6. 注意事项

### 6.1 性能考虑

- 使用 DotNet.Glob 替代 Regex，性能更好
- 属性注入会增加少量反射开销
- 使用 `NeedsPropertyInjection()` 检查，只对需要的类型启用工厂包装
- 考虑缓存反射结果以提升性能

### 6.2 生命周期注意

- Singleton 服务只会注入一次
- Scoped 服务每个请求注入一次
- Transient 服务每次创建都会注入

### 6.3 循环依赖

- 属性注入可能导致循环依赖更难发现
- 建议在开发环境启用循环依赖检测

## 7. 测试验证

实现后，运行 `Artisan.TestApp`，访问 `/api/diagnostics` 应返回：

```json
{
  "injectionStatus": {
    "userService": "OK",
    "productService": "OK",
    "cacheService": "OK",
    "appConfig": "OK",
    "appNameValue": "Artisan.TestApp (Development)"
  },
  "testResults": {
    "userCount": 3,
    "productCount": 3,
    "cacheInfo": "Memory: Memory, Redis: Redis (localhost:6379,password=redis123)"
  }
}
```

## 8. 参考资料

- [DotNet.Glob - NuGet](https://www.nuget.org/packages/DotNet.Glob)
- [DotNet.Glob - GitHub](https://github.com/dazinator/DotNet.Glob)
- [Microsoft File Globbing](https://learn.microsoft.com/en-us/dotnet/core/extensions/file-globbing)

---

**请确认设计方案或提出修改意见，确认后我将按照步骤实施。**
