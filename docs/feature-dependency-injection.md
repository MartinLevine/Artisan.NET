# Artisan.NET 依赖注入框架设计

## 1. 设计理念

**极简、快速接入、最少样板代码、所引即所得**

核心目标是封装 .NET 的样板启动代码，而不是重新发明轮子。

**核心理念：所引即所得**
- 用户不需要手写 `[DependsOn(typeof(RedisModule))]`
- 用户只要在 `.csproj` 里安装了 NuGet 包或引用了项目，框架就自动把对应模块加载进来并排好序
- 通过程序集引用推导依赖关系，实现 Spring Boot 式的"零配置"体验

```csharp
// 这就是一个完整的 Artisan.NET 应用
namespace MyApp;

[ArtisanApplication]
public class MyApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);  // 无需指定启动模块，自动发现！
    }
}

// 不需要 AppModule！框架自动从程序集引用发现所有模块

[Service]
public class UserService : IUserService
{
    [Inject] private IUserRepository Repository { get; set; }

    public User GetUser(int id) => Repository.FindById(id);
}
```

## 2. 程序集扫描策略："同心圆 + 特权通道"模型

### 2.1 核心模型

程序集扫描采用 **"同心圆 + 特权通道"** 机制：

```
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│   ┌───────────────────────────────────────────────────────────┐     │
│   │                                                           │     │
│   │   ┌───────────────────────────────────────────────────┐   │     │
│   │   │                                                   │   │     │
│   │   │   ┌───────────────────────────────────────────┐   │   │     │
│   │   │   │                                           │   │   │     │
│   │   │   │         入口程序集 (Entry)                 │   │   │     │
│   │   │   │         api-gateway                       │   │   │     │
│   │   │   │                                           │   │   │     │
│   │   │   └───────────────────────────────────────────┘   │   │     │
│   │   │                                                   │   │     │
│   │   │              引用的 Artisan 模块                   │   │     │
│   │   │    Artisan, ProCode.Hosting, MyCompany.Auth       │   │     │
│   │   │         (包含 ArtisanModule 的程序集)              │   │     │
│   │   │                                                   │   │     │
│   │   └───────────────────────────────────────────────────┘   │     │
│   │                                                           │     │
│   │                   其他引用的程序集                         │     │
│   │        Newtonsoft.Json, Serilog, AutoMapper...           │     │
│   │              (不包含 ArtisanModule)                       │     │
│   │                                                           │     │
│   └───────────────────────────────────────────────────────────┘     │
│                                                                     │
│   ════════════════════════════════════════════════════════════════  │
│                         特权通道 [ScanAssembly]                      │
│   ════════════════════════════════════════════════════════════════  │
│                                                                     │
│                      外部第三方库（强制扫描）                         │
│               ThirdParty.Services, Legacy.Components                │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 三层扫描机制

| 层级 | 名称 | 扫描条件 | 扫描方式 |
|------|------|---------|---------|
| **内圈** | 入口程序集 | 始终扫描 | 直接扫描整个程序集 |
| **中圈** | Artisan 模块 | 程序集包含 `ArtisanModule` 子类 | 递归引用链发现，直接扫描 |
| **特权通道** | 第三方库 | 用户显式 `[ScanAssembly]` | Glob 模式匹配程序集名 |

### 2.3 扫描决策表

```
程序集类型              │ 是否扫描 │ 扫描方式           │ 原因
────────────────────────┼──────────┼────────────────────┼─────────────────────────────
入口程序集              │ ✅ 是    │ 直接扫描           │ 用户代码入口
├── 引用: Artisan       │ ✅ 是    │ 直接扫描           │ 包含 ArtisanModule
├── 引用: ProCode.Auth  │ ✅ 是    │ 直接扫描           │ 包含 ArtisanModule
├── 引用: Newtonsoft    │ ❌ 否    │ -                  │ 无 ArtisanModule，非特权
├── 引用: System.*      │ ❌ 否    │ -                  │ 系统程序集
└── 引用: Microsoft.*   │ ❌ 否    │ -                  │ 系统程序集

[ScanAssembly] 指定     │ ✅ 是    │ Glob 匹配程序集名  │ 特权通道强制扫描
```

### 2.4 为什么不用 Glob 匹配用户项目？

**问题**：程序集名称和命名空间可能不一致！

```
程序集名称：api-gateway（带连字符）
RootNamespace：api_gateway（带下划线）

如果用 Glob 模式 "api-gateway.**" 去匹配 "api_gateway.Components"
→ 匹配失败！服务无法注册！
```

**正确做法**：
- 入口程序集和 Artisan 模块：直接通过程序集引用链发现，扫描整个程序集的所有类型
- 第三方库：仅当用户显式使用 `[ScanAssembly]` 时，才用 Glob 匹配**程序集名称**（不是命名空间）

### 2.5 扫描流程

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            程序集扫描流程                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. 入口点：从 [ArtisanApplication] 标记的类获取入口程序集                   │
│     var entryAssembly = typeof(Application).Assembly;                       │
│                                                                             │
│  2. 扫描入口程序集（内圈）：                                                 │
│     → 直接扫描整个程序集的所有公开类型                                        │
│     → 不需要任何 Glob 匹配                                                   │
│                                                                             │
│  3. 递归扫描引用链（中圈）：                                                 │
│     foreach (var refAssembly in assembly.GetReferencedAssemblies())         │
│     {                                                                       │
│         if (IsSystemAssembly(refAssembly)) continue;     // 跳过系统程序集   │
│         if (!ContainsArtisanModule(refAssembly)) continue; // 跳过无模块的   │
│         ScanAssembly(refAssembly);  // 直接扫描整个程序集                    │
│     }                                                                       │
│                                                                             │
│  4. 处理特权通道（[ScanAssembly]）：                                         │
│     foreach (var pattern in scanAssemblyPatterns)                           │
│     {                                                                       │
│         // 从 AppDomain 已加载的程序集中匹配                                  │
│         var matched = AppDomain.CurrentDomain.GetAssemblies()               │
│             .Where(a => GlobMatcher.IsMatch(pattern, a.GetName().Name));    │
│         foreach (var assembly in matched)                                   │
│             ScanAssembly(assembly);                                         │
│     }                                                                       │
│                                                                             │
│  5. 对每个被扫描的程序集，提取所有公开类型：                                  │
│     assembly.GetTypes()                                                     │
│       .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic)                │
│                                                                             │
│  6. 查找标记了以下特性的类型进行注册：                                        │
│     - [Service], [Repository], [Component], [Injectable]                   │
│     - [Module] (ArtisanModule 子类)                                         │
│     - [AppSetting] (配置类)                                                 │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.6 判断程序集是否包含 ArtisanModule

```csharp
/// <summary>
/// 检查程序集是否包含 ArtisanModule 子类
/// 用于决定是否扫描引用链中的程序集
/// </summary>
private static bool ContainsArtisanModule(Assembly assembly)
{
    try
    {
        return assembly.GetTypes()
            .Any(t => t.IsClass
                   && !t.IsAbstract
                   && typeof(ArtisanModule).IsAssignableFrom(t));
    }
    catch (ReflectionTypeLoadException)
    {
        return false;
    }
}
```

### 2.7 系统程序集过滤

以下前缀的程序集会被跳过：

```csharp
private static bool IsSystemAssembly(string? assemblyName)
{
    if (string.IsNullOrEmpty(assemblyName))
        return true;

    return assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
        || assemblyName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
        || assemblyName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
        || assemblyName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
        || assemblyName.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase)
        || assemblyName.StartsWith("PresentationCore", StringComparison.OrdinalIgnoreCase)
        || assemblyName.StartsWith("PresentationFramework", StringComparison.OrdinalIgnoreCase);
}
```

## 3. AssemblyScanner 实现

### 3.1 核心设计

基于"同心圆 + 特权通道"模型实现：

```csharp
using System.Reflection;
using Artisan.Modules;

namespace Artisan.DependencyInjection;

/// <summary>
/// 程序集扫描器
/// 实现"同心圆 + 特权通道"扫描机制
/// </summary>
public class AssemblyScanner : IAssemblyScanner
{
    private readonly List<Assembly> _scannedAssemblies = new();
    private readonly List<Type> _scannedTypes = new();
    private readonly HashSet<string> _processedAssemblies = new();

    /// <inheritdoc />
    public IReadOnlyList<Assembly> ScannedAssemblies => _scannedAssemblies;

    /// <inheritdoc />
    public IReadOnlyList<Type> ScannedTypes => _scannedTypes;

    /// <summary>
    /// 扫描入口程序集及其引用链
    /// </summary>
    /// <param name="entryAssembly">入口程序集（内圈）</param>
    /// <param name="additionalPatterns">特权通道：[ScanAssembly] 指定的第三方库模式</param>
    public void Scan(Assembly entryAssembly, IEnumerable<string>? additionalPatterns = null)
    {
        _scannedAssemblies.Clear();
        _scannedTypes.Clear();
        _processedAssemblies.Clear();

        // ========== 内圈：扫描入口程序集 ==========
        // 入口程序集始终完整扫描，不需要任何条件判断
        ScanAssemblyFully(entryAssembly);

        // ========== 中圈：扫描引用链中的 Artisan 模块 ==========
        // 只扫描包含 ArtisanModule 的程序集
        ScanReferencedModules(entryAssembly);

        // ========== 特权通道：扫描 [ScanAssembly] 指定的第三方库 ==========
        if (additionalPatterns != null)
        {
            ScanPrivilegedAssemblies(additionalPatterns);
        }
    }

    /// <summary>
    /// 完整扫描单个程序集的所有类型（内圈使用）
    /// </summary>
    private void ScanAssemblyFully(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name;
        if (assemblyName == null || _processedAssemblies.Contains(assemblyName))
            return;

        _processedAssemblies.Add(assemblyName);
        _scannedAssemblies.Add(assembly);

        // 扫描程序集中的所有公开类型
        CollectTypes(assembly);
    }

    /// <summary>
    /// 递归扫描引用链中包含 ArtisanModule 的程序集（中圈）
    /// </summary>
    private void ScanReferencedModules(Assembly assembly)
    {
        foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
        {
            // 跳过系统程序集
            if (IsSystemAssembly(referencedAssemblyName.Name))
                continue;

            // 跳过已处理的程序集
            if (_processedAssemblies.Contains(referencedAssemblyName.Name!))
                continue;

            try
            {
                var referencedAssembly = Assembly.Load(referencedAssemblyName);

                // 中圈条件：只扫描包含 ArtisanModule 的程序集
                if (ContainsArtisanModule(referencedAssembly))
                {
                    ScanAssemblyFully(referencedAssembly);
                    // 递归检查该模块的引用
                    ScanReferencedModules(referencedAssembly);
                }
                else
                {
                    // 即使不扫描，也标记为已处理，避免重复检查
                    _processedAssemblies.Add(referencedAssemblyName.Name!);
                }
            }
            catch (Exception)
            {
                // 忽略无法加载的程序集
            }
        }
    }

    /// <summary>
    /// 扫描特权通道指定的第三方库程序集
    /// 这是唯一使用 Glob 匹配的地方
    /// </summary>
    private void ScanPrivilegedAssemblies(IEnumerable<string> patterns)
    {
        var patternList = patterns.ToList();
        if (!patternList.Any())
            return;

        // 从 AppDomain 已加载的程序集中匹配
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in loadedAssemblies)
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null || _processedAssemblies.Contains(assemblyName))
                continue;

            if (IsSystemAssembly(assemblyName))
                continue;

            // 使用 Glob 模式匹配程序集名称（注意：匹配的是程序集名，不是命名空间）
            if (MatchesAnyPattern(assemblyName, patternList))
            {
                ScanAssemblyFully(assembly);
            }
        }
    }

    /// <summary>
    /// 收集程序集中的所有公开类型
    /// </summary>
    private void CollectTypes(Assembly assembly)
    {
        try
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic);

            foreach (var type in types)
            {
                _scannedTypes.Add(type);
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            // 处理类型加载异常，只添加能加载的类型
            var loadedTypes = ex.Types
                .Where(t => t != null && t.IsClass && !t.IsAbstract && t.IsPublic);
            foreach (var type in loadedTypes)
            {
                _scannedTypes.Add(type!);
            }
        }
    }

    /// <summary>
    /// 检查程序集是否包含 ArtisanModule 子类
    /// 这是中圈扫描的核心判断条件
    /// </summary>
    private static bool ContainsArtisanModule(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes()
                .Any(t => t.IsClass
                       && !t.IsAbstract
                       && typeof(ArtisanModule).IsAssignableFrom(t));
        }
        catch (ReflectionTypeLoadException)
        {
            return false;
        }
    }

    /// <summary>
    /// 检查程序集名称是否匹配任一 Glob 模式
    /// </summary>
    private static bool MatchesAnyPattern(string assemblyName, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (GlobMatcher.IsMatch(pattern, assemblyName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查是否是系统程序集
    /// </summary>
    private static bool IsSystemAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return true;

        return assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("PresentationCore", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("PresentationFramework", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IEnumerable<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
    {
        return _scannedTypes.Where(t => t.GetCustomAttribute<TAttribute>() != null);
    }

    /// <inheritdoc />
    public IEnumerable<Type> GetTypesAssignableTo<TBase>()
    {
        return GetTypesAssignableTo(typeof(TBase));
    }

    /// <inheritdoc />
    public IEnumerable<Type> GetTypesAssignableTo(Type baseType)
    {
        return _scannedTypes.Where(t =>
            baseType.IsAssignableFrom(t) && t != baseType);
    }
}
```

### 3.2 GlobMatcher（仅用于第三方库）

```csharp
using Microsoft.Extensions.FileSystemGlobbing;

namespace Artisan.DependencyInjection;

/// <summary>
/// Glob 模式匹配器
/// 仅用于匹配用户通过 [ScanAssembly] 指定的第三方库
/// </summary>
public static class GlobMatcher
{
    /// <summary>
    /// 检查名称是否匹配 Glob 模式
    /// </summary>
    public static bool IsMatch(string pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(name))
            return false;

        try
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern);

            // FileSystemGlobbing 针对路径设计，将程序集名称转换为路径格式
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

## 4. 服务注册（ServiceRegistrar）

### 4.1 核心设计

```csharp
namespace Artisan.DependencyInjection;

/// <summary>
/// 服务注册器
/// 负责扫描并注册所有标记了 DI 相关特性的类型
/// </summary>
public static class ServiceRegistrar
{
    /// <summary>
    /// 注册所有扫描到的服务
    /// </summary>
    public static void RegisterServices(
        IServiceCollection services,
        IEnumerable<Type> scannedTypes,
        IConfiguration configuration)
    {
        foreach (var type in scannedTypes)
        {
            // 注册 [Injectable], [Service], [Repository], [Component] 标记的类
            var injectableAttr = type.GetCustomAttribute<InjectableAttribute>();
            if (injectableAttr != null)
            {
                RegisterInjectableType(services, type, injectableAttr);
            }

            // 注册 [DynamicInjectable] 标记的方法
            RegisterDynamicInjectables(services, type, configuration);
        }
    }

    /// <summary>
    /// 注册可注入类型（带属性注入支持）
    /// </summary>
    private static void RegisterInjectableType(
        IServiceCollection services,
        Type implementationType,
        InjectableAttribute attribute)
    {
        var lifetime = ConvertLifetime(attribute.Lifetime);
        var interfaces = implementationType.GetInterfaces()
            .Where(i => !i.Namespace?.StartsWith("System") == true)
            .ToList();

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

        var hasInject = type.GetProperties(bindingFlags)
            .Any(p => p.GetCustomAttribute<InjectAttribute>() != null)
            || type.GetFields(bindingFlags)
            .Any(f => f.GetCustomAttribute<InjectAttribute>() != null);

        var hasAppSetting = type.GetProperties(bindingFlags)
            .Any(p => p.GetCustomAttribute<AppSettingAttribute>() != null);

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
        object Factory(IServiceProvider sp)
        {
            var instance = ActivatorUtilities.CreateInstance(sp, implementationType);
            var injector = sp.GetService<IPropertyInjector>();
            injector?.InjectProperties(instance);
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
            services.Add(new ServiceDescriptor(implementationType, Factory, lifetime));
        }
    }

    /// <summary>
    /// 标准服务注册（无属性注入）
    /// </summary>
    private static void RegisterStandard(
        IServiceCollection services,
        Type implementationType,
        List<Type> interfaces,
        ServiceLifetime lifetime,
        string? key)
    {
        if (key != null)
        {
            foreach (var iface in interfaces)
            {
                services.Add(new ServiceDescriptor(iface, key, implementationType, lifetime));
            }
            if (!interfaces.Any())
            {
                services.Add(new ServiceDescriptor(implementationType, key, implementationType, lifetime));
            }
        }
        else
        {
            foreach (var iface in interfaces)
            {
                services.Add(new ServiceDescriptor(iface, implementationType, lifetime));
            }
            services.Add(new ServiceDescriptor(implementationType, implementationType, lifetime));
        }
    }

    private static ServiceLifetime ConvertLifetime(Lifetime lifetime)
    {
        return lifetime switch
        {
            Lifetime.Transient => ServiceLifetime.Transient,
            Lifetime.Scoped => ServiceLifetime.Scoped,
            Lifetime.Singleton => ServiceLifetime.Singleton,
            _ => ServiceLifetime.Scoped
        };
    }
}
```

## 5. 属性注入（PropertyInjector）

### 5.1 问题：PropertyInjector 未被调用

即使服务被正确注册，ASP.NET Core 默认不会调用属性注入：

```
Controller 创建流程：
ControllerFactory → ControllerActivator → Controller
                    ↓
                    默认使用 DefaultControllerActivator
                    只支持构造函数注入，不调用 PropertyInjector

普通服务创建流程：
ServiceProvider → 直接构造 → Service
                  ↓
                  使用 ServiceDescriptor 中的工厂方法或直接构造
                  不调用 PropertyInjector
```

### 5.2 解决方案

| 场景 | 对象类型 | 需要的方案 |
|-----|---------|-----------|
| 1 | Controller | 自定义 `ArtisanControllerActivator` |
| 2 | Service/Repository/Component | 工厂包装（在 `ServiceRegistrar` 中实现） |

### 5.3 ArtisanControllerActivator

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

## 6. ArtisanApplication 启动器

### 6.1 启动流程

```csharp
namespace Artisan.Application;

public static class ArtisanApplication
{
    public static async Task RunAsync(string[] args)
    {
        // 1. 从调用栈获取入口类
        var entryType = FindEntryType();

        // 2. 获取入口程序集
        var entryAssembly = entryType.Assembly;

        // 3. 检查是否实现了 IConfigurableApplication
        IConfigurableApplication? configurableApp = null;
        if (typeof(IConfigurableApplication).IsAssignableFrom(entryType))
        {
            configurableApp = (IConfigurableApplication)Activator.CreateInstance(entryType)!;
        }

        // 阶段 1：框架预配置
        var artisanOptions = new ArtisanOptions();
        configurableApp?.ConfigureArtisan(artisanOptions);

        // 4. 获取额外的扫描模式（仅用于第三方库）
        var additionalPatterns = entryType.GetCustomAttributes<ScanAssemblyAttribute>()
            .Select(a => a.Pattern)
            .ToList();

        // 5. 构建 WebApplication
        var builder = WebApplication.CreateBuilder(args);

        // 6. 扫描程序集（直接扫描引用链，不需要 Glob 匹配）
        var scanner = new AssemblyScanner();
        scanner.Scan(entryAssembly, additionalPatterns);

        // 7. 注册服务
        builder.Services.AddArtisanServices(scanner, builder.Configuration);

        // 8. 自动发现所有模块
        var moduleLoader = new ModuleLoader();
        var modules = moduleLoader.LoadModules(
            entryAssembly,
            builder.Services,
            builder.Configuration,
            artisanOptions);

        // 阶段 2：服务注册
        foreach (var module in modules)
        {
            module.ConfigureServices(builder.Services);
        }
        configurableApp?.ConfigureServices(builder.Services);

        var app = builder.Build();

        // 阶段 3：中间件管道
        foreach (var module in modules)
        {
            module.Configure(app);
        }
        configurableApp?.Configure(app);

        await app.RunAsync();
    }
}
```

### 6.2 服务集合扩展

```csharp
namespace Artisan.DependencyInjection;

public static class ArtisanServiceCollectionExtensions
{
    public static IServiceCollection AddArtisanServices(
        this IServiceCollection services,
        IAssemblyScanner scanner,
        IConfiguration configuration)
    {
        // 注册配置系统
        ConfigurationRegistrar.RegisterAppSettings(services, configuration, scanner.ScannedTypes);

        // 注册属性注入器（必须先注册，因为服务注册时需要用到）
        services.AddSingleton<IPropertyInjector, PropertyInjector>();

        // 注册所有服务
        ServiceRegistrar.RegisterServices(services, scanner.ScannedTypes, configuration);

        // 注册 AssemblyScanner 作为单例
        services.AddSingleton<IAssemblyScanner>(scanner);

        // 注册自定义 Controller 激活器以支持属性注入
        services.AddSingleton<IControllerActivator, ArtisanControllerActivator>();

        return services;
    }
}
```

## 7. 使用示例

### 7.1 基本用法

```csharp
// Application.cs
namespace MyApp;

[ArtisanApplication]
public class Application
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);
    }
}
```

### 7.2 Service 定义

```csharp
// WeatherForecastService.cs
namespace MyApp.Services;

public interface IWeatherForecastService
{
    IEnumerable<WeatherForecast> GetForecasts();
}

[Service]
public class WeatherForecastService : IWeatherForecastService
{
    // 属性注入
    [Inject]
    private ILogger<WeatherForecastService> Logger { get; set; } = null!;

    // 配置注入
    [GetValue("App:Name")]
    private string AppName { get; set; } = "";

    public IEnumerable<WeatherForecast> GetForecasts()
    {
        Logger.LogInformation("[{AppName}] Getting weather forecasts", AppName);
        // ...
    }
}
```

### 7.3 Controller 使用

```csharp
// WeatherForecastController.cs
namespace MyApp.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    // 构造函数注入
    private readonly IWeatherForecastService _service;

    // 属性注入
    [Inject]
    public ILogger<WeatherForecastController>? Logger { get; set; }

    public WeatherForecastController(IWeatherForecastService service)
    {
        _service = service;
    }

    [HttpGet]
    public IEnumerable<WeatherForecast> Get()
    {
        Logger?.LogInformation("Getting weather forecast");
        return _service.GetForecasts();
    }
}
```

### 7.4 扫描第三方库（可选）

```csharp
namespace MyApp;

[ArtisanApplication]
[ScanAssembly("ThirdParty.Services.*")]  // 仅当需要扫描第三方库时
[ScanAssembly("MyCompany.SharedLib.**")]
public class Application
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);
    }
}
```

## 8. 文件变更清单

| 操作 | 文件路径 | 说明 |
|-----|---------|------|
| 重写 | `Artisan/DependencyInjection/AssemblyScanner.cs` | 移除 Glob 匹配，直接扫描引用链 |
| 保留 | `Artisan/DependencyInjection/GlobMatcher.cs` | 仅用于 `[ScanAssembly]` 第三方库匹配 |
| 修改 | `Artisan/DependencyInjection/ServiceRegistrar.cs` | 添加属性注入工厂包装 |
| 新增 | `Artisan/AspNetCore/ArtisanControllerActivator.cs` | Controller 属性注入支持 |
| 修改 | `Artisan/DependencyInjection/ArtisanServiceCollectionExtensions.cs` | 集成属性注入 |
| 修改 | `Artisan/Application/ArtisanApplication.cs` | 调整扫描流程 |

---

**请审阅此设计方案，确认后我将按照新设计重新实现。**
