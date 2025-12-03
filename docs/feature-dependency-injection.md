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

**用户的 `.csproj` - 所引即所得：**
```xml
<ItemGroup>
    <!-- 引用了这些包，对应的模块就会自动加载 -->
    <PackageReference Include="Artisan.Redis" Version="1.0.0" />
    <PackageReference Include="Artisan.OpenApi" Version="1.0.0" />
    <ProjectReference Include="..\MyProject.User\MyProject.User.csproj" />
</ItemGroup>
```

**对比原生 .NET 样板代码：**
```csharp
// 原生写法 - 每个项目都要写这些
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();
        app.Run();
    }
}
```

## 2. 项目结构

当前阶段使用单一项目，待框架完整落地后再考虑拆分：

```
Artisan/
├── src/
│   └── Artisan/
│       ├── Artisan.csproj
│       ├── Application/           # 应用启动器
│       ├── Attributes/            # 所有 Attributes
│       ├── Configuration/         # 配置系统
│       ├── DependencyInjection/   # DI 核心
│       ├── Modules/               # 模块系统
│       ├── Session/               # Session 生命周期
│       └── Aop/                   # AOP 支持
└── tests/
    └── Artisan.Tests/
```

## 3. 核心 API 设计

### 3.1 Attributes 总览

| Attribute | 用途 | 默认值 | 示例 |
|-----------|------|--------|------|
| `[ArtisanApplication]` | 应用入口，自动扫描当前命名空间 | - | `[ArtisanApplication] class MyApp` |
| `[ScanAssembly]` | 扫描额外的第三方库 | - | `[ScanAssembly("ThirdParty.*")]` |
| `[Inject]` | 注入依赖 | - | `[Inject("redis")] ICache Cache` |
| `[Injectable]` | 标记可注入类 | Scoped | `[Injectable("key", Lifetime.Singleton)]` |
| `[Service]` | 服务层 | Scoped | `[Service] class UserService` |
| `[Repository]` | 数据层 | Scoped | `[Repository] class UserRepo` |
| `[Component]` | 通用组件 | Singleton | `[Component] class CacheHelper` |
| `[Module]` | 模块配置（支持自动发现） | Level=Application | `[Module(Level = ModuleLevel.Infrastructure)]` |
| `[DynamicInjectable]` | 动态注入（方法级） | Singleton | `[DynamicInjectable] ICache CreateCache()` |
| `[AppSetting]` | 配置类/配置节映射 | - | `[AppSetting("Database")] class DbConfig` |
| `[GetValue]` | 获取单个配置值 | - | `[GetValue("App:Name")] string AppName` |

### 3.2 模块层级定义

```csharp
namespace Artisan.Modules;

/// <summary>
/// 模块层级 - 用于自动排序，无需显式声明依赖
/// </summary>
public enum ModuleLevel
{
    Kernel = 0,          // 核心底座 (Logger, EventBus)
    Infrastructure = 10, // 基础设施 (Redis, Database, Session)
    Application = 20,    // 业务模块 (User, Order)
    Presentation = 100   // 顶层入口（一般不需要，框架自动处理）
}
```

### 3.3 生命周期定义

```csharp
namespace Artisan.DependencyInjection;

public enum Lifetime
{
    Transient,   // 每次注入创建新实例
    Scoped,      // 每个请求一个实例
    Singleton    // 全局单例
}
```

### 3.4 语义化 Attribute 默认生命周期

| Attribute | 默认生命周期 | 可选生命周期 | 说明 |
|-----------|-------------|-------------|------|
| `[Service]` | Scoped | Transient, Singleton | 业务逻辑层，每个请求独立 |
| `[Repository]` | Scoped | Transient, Singleton | 数据访问层，每个请求独立 |
| `[Component]` | Singleton | Transient, Scoped | 无状态工具类，全局共享 |

**Session 生命周期：** 通过 `[Component(SessionScoped = true)]` 指定，用于需要跟随用户会话的有状态组件（如购物车、用户偏好设置等）。

## 4. 详细设计

### 4.1 核心 Attributes

```csharp
namespace Artisan.Core.Attributes;

/// <summary>
/// 标记一个类可以被注入到 DI 容器
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class InjectableAttribute : Attribute
{
    public string? Key { get; }
    public Lifetime Lifetime { get; set; } = Lifetime.Scoped;

    public InjectableAttribute(string? key = null)
    {
        Key = key;
    }
}

/// <summary>
/// 注入依赖到属性或字段
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class InjectAttribute : Attribute
{
    public string? Key { get; }
    public bool Required { get; set; } = true;

    public InjectAttribute(string? key = null)
    {
        Key = key;
    }
}
```

### 4.2 语义化组件 Attributes

```csharp
namespace Artisan.Core.Attributes;

/// <summary>
/// 服务层组件，默认 Scoped 生命周期
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ServiceAttribute : InjectableAttribute
{
    public ServiceAttribute(string? key = null) : base(key)
    {
        Lifetime = Lifetime.Scoped;
    }
}

/// <summary>
/// 数据访问层组件，默认 Scoped 生命周期
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RepositoryAttribute : InjectableAttribute
{
    public RepositoryAttribute(string? key = null) : base(key)
    {
        Lifetime = Lifetime.Scoped;
    }
}

/// <summary>
/// 通用组件，默认 Singleton 生命周期
/// 适用于无状态的工具类、帮助类
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ComponentAttribute : InjectableAttribute
{
    /// <summary>
    /// 是否跟随 HTTP Session 生命周期
    /// 设为 true 时，每个用户会话拥有独立实例
    /// </summary>
    public bool SessionScoped { get; set; } = false;

    public ComponentAttribute(string? key = null) : base(key)
    {
        Lifetime = Lifetime.Singleton;
    }
}
```

**覆盖默认生命周期示例：**
```csharp
// Service 默认是 Scoped，但可以覆盖为 Singleton
[Service(Lifetime = Lifetime.Singleton)]
public class CacheService : ICacheService { }

// Component 使用 Session 生命周期
[Component(SessionScoped = true)]
public class ShoppingCart : IShoppingCart { }
```

### 4.3 模块系统（Module）- 自动发现与程序集引用推导

模块系统封装 .NET 的服务注册和中间件配置，采用**自动发现 + 程序集引用推导**策略，实现"所引即所得"的零配置体验。

#### 4.3.1 核心理念

**放弃强制的显式依赖**：用户不需要手写 `DependsOn`，框架自动从程序集引用推导依赖关系。

**逻辑推导**：如果 `MyApp.dll` 引用了 `Artisan.Redis.dll`，那么物理上 `MyApp` 一定是依赖 `Redis` 的。我们利用这个物理事实来生成逻辑依赖。

#### 4.3.2 ModuleAttribute 设计

```csharp
namespace Artisan.Modules;

/// <summary>
/// 模块层级 - 用于自动排序，无需显式声明依赖
/// </summary>
public enum ModuleLevel
{
    Kernel = 0,          // 核心底座 (Logger, EventBus)
    Infrastructure = 10, // 基础设施 (Redis, Database, Session, Auth)
    Application = 20,    // 业务模块 (User, Order)
    Presentation = 100   // 顶层入口（一般不需要，框架自动处理）
}

/// <summary>
/// 标记一个类为 Artisan 模块
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ModuleAttribute : Attribute
{
    /// <summary>
    /// 模块层级，用于自动排序
    /// 低层级模块先于高层级模块加载
    /// </summary>
    public ModuleLevel Level { get; set; } = ModuleLevel.Application;

    /// <summary>
    /// 同层级内的微调顺序（数值小的先执行）
    /// 例如：Session(Order=10) 先于 Auth(Order=20)
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// 显式依赖（可选）- 框架开发者的底层工具
    /// 普通用户不需要使用，依赖关系会自动从程序集引用推导
    /// </summary>
    public Type[] DependsOn { get; set; } = [];
}

/// <summary>
/// 模块基类
/// </summary>
public abstract class ArtisanModule
{
    /// <summary>
    /// 配置服务（对应 builder.Services.AddXxx）
    /// 通过构造函数注入配置类（使用 [AppSetting] 标记的类）
    /// </summary>
    public virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    /// 配置中间件（对应 app.UseXxx）
    /// </summary>
    public virtual void Configure(WebApplication app) { }
}

/// <summary>
/// 循环依赖异常
/// </summary>
public class CircularDependencyException : Exception
{
    public Type[] DependencyChain { get; }

    public CircularDependencyException(Type[] chain)
        : base($"Circular dependency detected: {string.Join(" -> ", chain.Select(t => t.Name))}")
    {
        DependencyChain = chain;
    }
}
```

#### 4.3.3 模块加载策略

**自动发现算法：**
1. **入口扫描**：从 `EntryAssembly`（用户的应用程序）开始
2. **递归加载程序集**：通过 `GetReferencedAssemblies()` 获取所有引用的程序集
3. **过滤 Artisan 模块**：只加载包含 `ArtisanModule` 子类的程序集
4. **构建隐式依赖**：
   - 如果程序集 A 引用了程序集 B
   - 且程序集 A 中有 `ModuleA`，程序集 B 中有 `ModuleB`
   - 自动建立边：`ModuleA` → 依赖 → `ModuleB`
5. **排序**：先按 `Level` 排序，同级再按 `Order` 排序，最后按隐式/显式依赖拓扑排序

#### 4.3.4 框架内置模块示例

**框架作者编写模块时使用 Level 和 Order：**

```csharp
// ========== 基础设施层模块 ==========

// Session 模块 - Level=Infrastructure, Order=10
[Module(Level = ModuleLevel.Infrastructure, Order = 10)]
public class SessionModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
        services.AddSession();
    }

    public override void Configure(WebApplication app)
    {
        app.UseSession();  // Session 必须在 Auth 之前
    }
}

// Auth 模块 - Level=Infrastructure, Order=20（在 Session 之后）
[Module(Level = ModuleLevel.Infrastructure, Order = 20)]
public class AuthModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddAuthentication();
        services.AddAuthorization();
    }

    public override void Configure(WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}

// Redis 模块 - 通过构造函数注入配置
[Module(Level = ModuleLevel.Infrastructure)]
public class RedisModule : ArtisanModule
{
    private readonly CacheSettings _settings;

    public RedisModule(CacheSettings settings)
    {
        _settings = settings;
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = _settings.RedisConnection;
        });
    }
}

// WebApi 模块
[Module(Level = ModuleLevel.Infrastructure)]
public class WebApiModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
    }

    public override void Configure(WebApplication app)
    {
        app.UseHttpsRedirection();
        app.MapControllers();
    }
}

// OpenApi 模块
[Module(Level = ModuleLevel.Infrastructure)]
public class OpenApiModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddOpenApi();
    }

    public override void Configure(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
    }
}
```

#### 4.3.5 用户模块示例（零配置）

**用户不需要写任何 DependsOn！**

```csharp
// ========== 用户的业务模块 ==========

// 用户模块 - 默认 Level=Application
[Module]
public class UserModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册用户相关服务
    }
}

// 订单模块
[Module]
public class OrderModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册订单相关服务
    }
}
```

**用户的 .csproj - 所引即所得：**
```xml
<ItemGroup>
    <!-- 引用了这些包，对应的模块就会自动加载并排好序 -->
    <PackageReference Include="Artisan.WebApi" Version="1.0.0" />
    <PackageReference Include="Artisan.OpenApi" Version="1.0.0" />
    <PackageReference Include="Artisan.Redis" Version="1.0.0" />
    <PackageReference Include="Artisan.Auth" Version="1.0.0" />
    <ProjectReference Include="..\MyProject.User\MyProject.User.csproj" />
    <ProjectReference Include="..\MyProject.Order\MyProject.Order.csproj" />
</ItemGroup>
```

**自动推导的加载顺序：**
1. `SessionModule` (Infrastructure, Order=10)
2. `AuthModule` (Infrastructure, Order=20)
3. `RedisModule` (Infrastructure, Order=0)
4. `WebApiModule` (Infrastructure, Order=0)
5. `OpenApiModule` (Infrastructure, Order=0)
6. `UserModule` (Application)
7. `OrderModule` (Application)

#### 4.3.6 显式依赖（高级用法）

在极少数情况下，如果程序集引用无法表达依赖关系，仍可使用显式依赖：

```csharp
// 强制 PaymentModule 在 OrderModule 之后加载
// 即使它们在不同的程序集且没有直接引用关系
[Module(DependsOn = [typeof(OrderModule)])]
public class PaymentModule : ArtisanModule { }
```

**循环依赖检测：**
```csharp
// 这会抛出 CircularDependencyException
[Module(DependsOn = [typeof(ModuleB)])]
public class ModuleA : ArtisanModule { }

[Module(DependsOn = [typeof(ModuleA)])]  // 循环依赖！
public class ModuleB : ArtisanModule { }

// 异常信息：Circular dependency detected: ModuleA -> ModuleB -> ModuleA
```

### 4.4 应用入口与程序集扫描

```csharp
namespace Artisan.Attributes;

/// <summary>
/// 标记应用入口类
/// 自动扫描该类所在命名空间及其所有子命名空间
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ArtisanApplicationAttribute : Attribute
{
    /// <summary>
    /// 是否扫描子命名空间（默认 true）
    /// </summary>
    public bool ScanSubNamespaces { get; set; } = true;
}

/// <summary>
/// 标记需要额外扫描的第三方库命名空间（Glob 模式）
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ScanAssemblyAttribute : Attribute
{
    public string Pattern { get; }

    public ScanAssemblyAttribute(string pattern)
    {
        Pattern = pattern;
    }
}
```

**Glob 语法支持：**
```
*     - 匹配任意字符（不含点）
**    - 匹配任意字符（含点）
?     - 匹配单个字符
[abc] - 匹配字符集
```

**使用示例：**
```csharp
namespace MyApp;

// 基本用法 - 无需指定启动模块，框架自动发现所有模块
[ArtisanApplication]
public class MyApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);  // 自动发现并加载所有模块
    }
}

// 需要扫描第三方库时
[ArtisanApplication]
[ScanAssembly("ThirdParty.Services.*")]
[ScanAssembly("MyCompany.SharedLib.**")]
public class MyApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);
    }
}
```

### 4.5 动态注入 (类似 @Bean)

```csharp
namespace Artisan.Attributes;

/// <summary>
/// 标记一个方法为动态依赖提供者（类似 Spring @Bean）
/// 方法可以放在任何被扫描到的类中（如标记了 [AppSetting] 的配置类）
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class DynamicInjectableAttribute : Attribute
{
    public string? Key { get; }
    public Lifetime Lifetime { get; set; } = Lifetime.Singleton;

    public DynamicInjectableAttribute(string? key = null)
    {
        Key = key;
    }
}
```

**使用示例：**
```csharp
// 在配置类中定义动态注入
[AppSetting("Cache")]
public class CacheConfig
{
    public string RedisConnection { get; set; } = "";
    public int DefaultExpireSeconds { get; set; } = 60;

    [DynamicInjectable("redis")]
    public ICache CreateRedisCache()
    {
        return new RedisCache(RedisConnection);
    }

    [DynamicInjectable("memory", Lifetime = Lifetime.Singleton)]
    public ICache CreateMemoryCache()
    {
        return new MemoryCache();
    }
}

// 也可以在 Component 中定义
[Component]
public class CacheFactory
{
    [GetValue("Cache:RedisConnection")]
    private string RedisConnection { get; set; } = "";

    [DynamicInjectable("redis")]
    public ICache CreateRedisCache()
    {
        return new RedisCache(RedisConnection);
    }
}

// 使用
[Service]
public class ProductService
{
    [Inject("redis")] private ICache Cache { get; set; }
}
```

### 4.6 配置系统

配置系统底层复用 .NET 的 `IOptions<T>` 机制，支持嵌套对象绑定和热更新（Hot Reload）。

```csharp
namespace Artisan.Configuration;

/// <summary>
/// 将配置节映射到类，或标记属性从指定配置节注入
/// 底层使用 IOptions<T> / IOptionsMonitor<T> 实现
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class AppSettingAttribute : Attribute
{
    public string Section { get; }

    /// <summary>
    /// 是否启用热更新（默认 false，使用 IOptions）
    /// 设为 true 时使用 IOptionsMonitor，配置文件变更后自动更新
    /// </summary>
    public bool HotReload { get; set; } = false;

    public AppSettingAttribute(string section)
    {
        Section = section;
    }
}

/// <summary>
/// 获取单个配置值
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class GetValueAttribute : Attribute
{
    public string Key { get; }
    public object? DefaultValue { get; set; }

    public GetValueAttribute(string key)
    {
        Key = key;
    }
}

/// <summary>
/// 配置访问接口（可选，用于动态获取配置）
/// </summary>
public interface IAppSettings
{
    T? GetValue<T>(string key);
    T GetValue<T>(string key, T defaultValue);
    T? GetSection<T>(string section) where T : class, new();
    bool Exists(string key);
}
```

**配置注册实现（复用 IOptions<T>）：**
```csharp
internal static class ConfigurationRegistrar
{
    public static void RegisterAppSettings(IServiceCollection services, IConfiguration configuration)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var appSettingAttr = type.GetCustomAttribute<AppSettingAttribute>();
                    if (appSettingAttr != null)
                    {
                        // 使用 .NET 原生的 Options 绑定机制
                        var section = configuration.GetSection(appSettingAttr.Section);

                        // 注册 IOptions<T>（支持嵌套对象自动绑定）
                        var configureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
                            .GetMethod("Configure", [typeof(IServiceCollection), typeof(IConfiguration)])!
                            .MakeGenericMethod(type);
                        configureMethod.Invoke(null, [services, section]);

                        // 同时注册类型本身，方便直接注入
                        services.AddSingleton(type, sp =>
                        {
                            var optionsType = typeof(IOptions<>).MakeGenericType(type);
                            var options = sp.GetRequiredService(optionsType);
                            return optionsType.GetProperty("Value")!.GetValue(options)!;
                        });
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // 忽略无法加载的程序集
            }
        }
    }
}
```

**使用示例（支持嵌套对象）：**
```csharp
// appsettings.json - 嵌套配置
{
  "Database": {
    "ConnectionString": "Server=localhost;Database=mydb",
    "MaxPoolSize": 100,
    "Retry": {
      "MaxAttempts": 3,
      "DelayMs": 1000
    },
    "ReadReplicas": [
      { "Host": "replica1", "Port": 5432 },
      { "Host": "replica2", "Port": 5432 }
    ]
  }
}

// 配置类（支持嵌套对象和集合）
[AppSetting("Database")]
public class DatabaseConfig
{
    public string ConnectionString { get; set; } = "";
    public int MaxPoolSize { get; set; } = 10;
    public RetryConfig Retry { get; set; } = new();           // 嵌套对象
    public List<ReplicaConfig> ReadReplicas { get; set; } = new();  // 集合
}

public class RetryConfig
{
    public int MaxAttempts { get; set; } = 3;
    public int DelayMs { get; set; } = 1000;
}

public class ReplicaConfig
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5432;
}

// 在服务中使用
[Service]
public class DataService
{
    // 方式1：直接注入配置类
    [AppSetting("Database")]
    private DatabaseConfig DbConfig { get; set; } = null!;

    // 方式2：注入 IOptions<T>（.NET 原生方式）
    private readonly DatabaseConfig _config;

    public DataService(IOptions<DatabaseConfig> options)
    {
        _config = options.Value;
    }

    // 方式3：注入 IOptionsMonitor<T>（支持热更新）
    public DataService(IOptionsMonitor<DatabaseConfig> optionsMonitor)
    {
        // 配置变更时自动获取最新值
        optionsMonitor.OnChange(config => Console.WriteLine($"Config updated: {config.ConnectionString}"));
    }

    // 获取单个值
    [GetValue("App:Name")]
    private string AppName { get; set; } = "";

    [GetValue("App:MaxRetry", DefaultValue = 3)]
    private int MaxRetry { get; set; }
}
```

### 4.7 可配置应用接口

用户的 Application 类可以实现 `IConfigurableApplication` 接口，参与框架的配置流程。框架会将其隐式当作 Module 处理。

```csharp
namespace Artisan.Application;

/// <summary>
/// 可配置应用接口
/// 用户的 Application 类可选实现此接口，参与框架配置流程
/// 框架会将实现此接口的 Application 类隐式当作 Module 处理
/// </summary>
public interface IConfigurableApplication
{
    // ==========================================
    // 阶段 1：框架预配置 (最先执行)
    // ==========================================
    /// <summary>
    /// 配置 Artisan 框架选项
    /// 职责：禁用/替换自动加载的模块、配置框架底层行为
    /// 时机：WebApplicationBuilder 创建之前
    /// </summary>
    void ConfigureArtisan(ArtisanOptions options) { }

    // ==========================================
    // 阶段 2：服务注册 (中间执行)
    // ==========================================
    /// <summary>
    /// 配置服务
    /// 职责：注册 DI 服务 (builder.Services.AddXxx)
    /// 时机：ModuleLoader 扫描之后，builder.Build() 之前
    /// </summary>
    void ConfigureServices(IServiceCollection services) { }

    // ==========================================
    // 阶段 3：中间件管道 (最后执行)
    // ==========================================
    /// <summary>
    /// 配置应用
    /// 职责：配置 HTTP 管道 (app.UseXxx)
    /// 时机：builder.Build() 之后
    /// </summary>
    void Configure(WebApplication app) { }
}

/// <summary>
/// Artisan 框架配置选项
/// </summary>
public class ArtisanOptions
{
    // 被禁用的模块类型集合
    private readonly HashSet<Type> _disabledModules = new();

    // 模块替换映射：原模块类型 -> 替换模块类型
    private readonly Dictionary<Type, Type> _moduleReplacements = new();

    /// <summary>
    /// 禁用指定模块（该模块将不会被加载）
    /// </summary>
    /// <typeparam name="TModule">要禁用的模块类型</typeparam>
    public ArtisanOptions DisableModule<TModule>() where TModule : ArtisanModule
    {
        _disabledModules.Add(typeof(TModule));
        return this;
    }

    /// <summary>
    /// 禁用指定模块（该模块将不会被加载）
    /// </summary>
    /// <param name="moduleType">要禁用的模块类型</param>
    public ArtisanOptions DisableModule(Type moduleType)
    {
        if (!typeof(ArtisanModule).IsAssignableFrom(moduleType))
            throw new ArgumentException($"{moduleType.Name} must inherit from ArtisanModule");

        _disabledModules.Add(moduleType);
        return this;
    }

    /// <summary>
    /// 替换模块（用自定义模块替换框架默认模块）
    /// </summary>
    /// <typeparam name="TOriginal">原模块类型</typeparam>
    /// <typeparam name="TReplacement">替换模块类型</typeparam>
    public ArtisanOptions ReplaceModule<TOriginal, TReplacement>()
        where TOriginal : ArtisanModule
        where TReplacement : ArtisanModule
    {
        _moduleReplacements[typeof(TOriginal)] = typeof(TReplacement);
        return this;
    }

    /// <summary>
    /// 替换模块（用自定义模块替换框架默认模块）
    /// </summary>
    public ArtisanOptions ReplaceModule(Type originalType, Type replacementType)
    {
        if (!typeof(ArtisanModule).IsAssignableFrom(originalType))
            throw new ArgumentException($"{originalType.Name} must inherit from ArtisanModule");
        if (!typeof(ArtisanModule).IsAssignableFrom(replacementType))
            throw new ArgumentException($"{replacementType.Name} must inherit from ArtisanModule");

        _moduleReplacements[originalType] = replacementType;
        return this;
    }

    // 内部方法：供 ModuleLoader 使用
    internal bool IsModuleDisabled(Type moduleType) => _disabledModules.Contains(moduleType);
    internal Type? GetReplacementModule(Type moduleType) =>
        _moduleReplacements.TryGetValue(moduleType, out var replacement) ? replacement : null;
    internal IReadOnlySet<Type> DisabledModules => _disabledModules;
    internal IReadOnlyDictionary<Type, Type> ModuleReplacements => _moduleReplacements;
}
```

**使用示例：**
```csharp
namespace MyApp;

[ArtisanApplication]
public class MyApplication : IConfigurableApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);
    }

    // 阶段 1：框架预配置
    public void ConfigureArtisan(ArtisanOptions options)
    {
        // 禁用框架自带的 OpenApi 模块
        options.DisableModule<OpenApiModule>();

        // 用自定义的 Redis 模块替换框架默认的
        options.ReplaceModule<RedisModule, MyCustomRedisModule>();
    }

    // 阶段 2：服务注册
    public void ConfigureServices(IServiceCollection services)
    {
        // 注册应用级别的服务
        services.AddSingleton<IMyCustomService, MyCustomService>();

        // 添加自定义的 Swagger 配置
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "My API", Version = "v1" });
        });
    }

    // 阶段 3：中间件管道
    public void Configure(WebApplication app)
    {
        // 添加自定义中间件
        app.UseSwagger();
        app.UseSwaggerUI();

        // 添加自定义端点
        app.MapGet("/health", () => "OK");
    }
}

// 自定义 Redis 模块（替换框架默认的）
[Module(Level = ModuleLevel.Infrastructure)]
public class MyCustomRedisModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // 使用不同的 Redis 客户端库
        services.AddSingleton<IDistributedCache, MyCustomRedisCache>();
    }
}
```

### 4.8 Application 启动器

```csharp
namespace Artisan.Application;

/// <summary>
/// Artisan 应用启动器
/// 核心职责：封装 .NET 样板启动代码，自动发现并加载所有模块
/// </summary>
public static class ArtisanApplication
{
    /// <summary>
    /// 启动应用（自动发现模块，无需指定启动模块）
    /// </summary>
    public static void Run(string[] args)
    {
        RunAsync(args).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步启动应用
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        // 1. 从调用栈获取入口类（标记了 [ArtisanApplication] 的类）
        var entryType = FindEntryType();

        // 2. 验证 [ArtisanApplication] 特性
        var appAttr = entryType.GetCustomAttribute<ArtisanApplicationAttribute>()
            ?? throw new InvalidOperationException($"{entryType.Name} must have [ArtisanApplication] attribute");

        // 3. 检查是否实现了 IConfigurableApplication 接口
        IConfigurableApplication? configurableApp = null;
        if (typeof(IConfigurableApplication).IsAssignableFrom(entryType))
        {
            configurableApp = (IConfigurableApplication)Activator.CreateInstance(entryType)!;
        }

        // ==========================================
        // 阶段 1：框架预配置（最先执行）
        // ==========================================
        var artisanOptions = new ArtisanOptions();
        configurableApp?.ConfigureArtisan(artisanOptions);

        // 4. 构建扫描模式列表
        var scanPatterns = BuildScanPatterns(entryType, appAttr);

        // 5. 构建 WebApplication
        var builder = WebApplication.CreateBuilder(args);

        // 6. 扫描并注册 [Service], [Repository], [Component], [Injectable] 等
        builder.Services.AddArtisanServices(scanPatterns);

        // 7. 自动发现所有模块并解析依赖链（基于程序集引用推导）
        //    传入 ArtisanOptions 以支持禁用/替换模块
        var entryAssembly = entryType.Assembly;
        var moduleLoader = new ModuleLoader();
        var modules = moduleLoader.LoadModules(
            entryAssembly,
            builder.Services,
            builder.Configuration,
            artisanOptions);  // 传入配置选项

        // ==========================================
        // 阶段 2：服务注册（中间执行）
        // ==========================================

        // 8. 按依赖顺序调用模块的 ConfigureServices
        foreach (var module in modules)
        {
            module.ConfigureServices(builder.Services);
        }

        // 9. 调用用户 Application 的 ConfigureServices（最后执行，可覆盖模块配置）
        configurableApp?.ConfigureServices(builder.Services);

        var app = builder.Build();

        // ==========================================
        // 阶段 3：中间件管道（最后执行）
        // ==========================================

        // 10. 按依赖顺序调用模块的 Configure
        foreach (var module in modules)
        {
            module.Configure(app);
        }

        // 11. 调用用户 Application 的 Configure（最后执行，可覆盖模块配置）
        configurableApp?.Configure(app);

        await app.RunAsync();
    }

    private static string[] BuildScanPatterns(Type entryType, ArtisanApplicationAttribute appAttr)
    {
        var patterns = new List<string>();

        // 自动添加应用类所在命名空间
        var baseNamespace = entryType.Namespace;
        if (!string.IsNullOrEmpty(baseNamespace))
        {
            patterns.Add(appAttr.ScanSubNamespaces ? $"{baseNamespace}.**" : baseNamespace);
        }

        // 添加额外的 [ScanAssembly] 模式
        var extraPatterns = entryType.GetCustomAttributes<ScanAssemblyAttribute>()
            .Select(a => a.Pattern);
        patterns.AddRange(extraPatterns);

        return patterns.ToArray();
    }

    private static Type FindEntryType()
    {
        var stackTrace = new StackTrace();
        foreach (var frame in stackTrace.GetFrames())
        {
            var method = frame.GetMethod();
            if (method?.Name == "Main" && method.DeclaringType != null)
            {
                if (method.DeclaringType.GetCustomAttribute<ArtisanApplicationAttribute>() != null)
                {
                    return method.DeclaringType;
                }
            }
        }
        throw new InvalidOperationException("Cannot find entry type with [ArtisanApplication] attribute");
    }
}

/// <summary>
/// 模块加载器 - 自动发现模块并基于程序集引用推导依赖关系
/// </summary>
internal class ModuleLoader
{
    // 缓存：程序集 -> 该程序集中的模块类型
    private readonly Dictionary<Assembly, Type?> _assemblyModuleCache = new();

    // 依赖图：模块类型 -> 依赖的模块类型列表
    private readonly Dictionary<Type, List<Type>> _dependencyGraph = new();

    // 框架配置选项
    private ArtisanOptions _options = new();

    /// <summary>
    /// 加载所有模块（自动发现 + 程序集引用推导）
    /// </summary>
    public List<ArtisanModule> LoadModules(
        Assembly entryAssembly,
        IServiceCollection services,
        IConfiguration configuration,
        ArtisanOptions? options = null)
    {
        _options = options ?? new ArtisanOptions();

        // 1. 递归扫描所有引用的程序集，发现所有模块
        var allModuleTypes = FindAllModulesInReferencedAssemblies(entryAssembly);

        // 2. 过滤被禁用的模块
        allModuleTypes = FilterDisabledModules(allModuleTypes);

        // 3. 应用模块替换
        allModuleTypes = ApplyModuleReplacements(allModuleTypes);

        // 4. 构建隐式依赖图（基于程序集引用关系）
        BuildImplicitDependencyGraph(allModuleTypes);

        // 5. 添加显式依赖（DependsOn）
        AddExplicitDependencies(allModuleTypes);

        // 6. 拓扑排序（先按 Level/Order，再按依赖关系）
        var sortedModuleTypes = TopologicalSort(allModuleTypes);

        // 7. 实例化模块（通过 DI 注入配置类）
        return InstantiateModules(sortedModuleTypes, services, configuration);
    }

    /// <summary>
    /// 过滤被禁用的模块
    /// </summary>
    private List<Type> FilterDisabledModules(List<Type> moduleTypes)
    {
        return moduleTypes
            .Where(t => !_options.IsModuleDisabled(t))
            .ToList();
    }

    /// <summary>
    /// 应用模块替换
    /// </summary>
    private List<Type> ApplyModuleReplacements(List<Type> moduleTypes)
    {
        var result = new List<Type>();

        foreach (var moduleType in moduleTypes)
        {
            var replacement = _options.GetReplacementModule(moduleType);
            if (replacement != null)
            {
                // 使用替换模块，但只添加一次
                if (!result.Contains(replacement))
                {
                    result.Add(replacement);
                }
            }
            else
            {
                result.Add(moduleType);
            }
        }

        // 添加替换模块中那些不在原列表中的模块（新增的替换模块）
        foreach (var replacement in _options.ModuleReplacements.Values)
        {
            if (!result.Contains(replacement))
            {
                result.Add(replacement);
            }
        }

        return result;
    }

    /// <summary>
    /// 递归扫描所有引用的程序集，找到所有包含 ArtisanModule 的模块
    /// </summary>
    private List<Type> FindAllModulesInReferencedAssemblies(Assembly entryAssembly)
    {
        var allModules = new List<Type>();
        var visitedAssemblies = new HashSet<string>();
        var assemblyQueue = new Queue<Assembly>();

        assemblyQueue.Enqueue(entryAssembly);

        while (assemblyQueue.Count > 0)
        {
            var assembly = assemblyQueue.Dequeue();
            var assemblyName = assembly.GetName().Name;

            if (assemblyName == null || visitedAssemblies.Contains(assemblyName))
                continue;

            visitedAssemblies.Add(assemblyName);

            // 跳过系统程序集
            if (IsSystemAssembly(assemblyName))
                continue;

            // 查找该程序集中的模块
            var moduleType = FindModuleInAssembly(assembly);
            if (moduleType != null)
            {
                allModules.Add(moduleType);
                _assemblyModuleCache[assembly] = moduleType;
            }

            // 递归扫描引用的程序集
            foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
            {
                if (IsSystemAssembly(referencedAssemblyName.Name))
                    continue;

                try
                {
                    var referencedAssembly = Assembly.Load(referencedAssemblyName);
                    assemblyQueue.Enqueue(referencedAssembly);
                }
                catch (Exception)
                {
                    // 忽略无法加载的程序集
                }
            }
        }

        return allModules;
    }

    /// <summary>
    /// 在程序集中查找 ArtisanModule 子类
    /// </summary>
    private Type? FindModuleInAssembly(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes()
                .FirstOrDefault(t =>
                    t.IsClass &&
                    !t.IsAbstract &&
                    typeof(ArtisanModule).IsAssignableFrom(t) &&
                    t.GetCustomAttribute<ModuleAttribute>() != null);
        }
        catch (ReflectionTypeLoadException)
        {
            return null;
        }
    }

    /// <summary>
    /// 基于程序集引用关系构建隐式依赖图
    /// 如果程序集 A 引用了程序集 B，且两者都有模块，则 ModuleA 依赖 ModuleB
    /// </summary>
    private void BuildImplicitDependencyGraph(List<Type> allModuleTypes)
    {
        // 初始化依赖图
        foreach (var moduleType in allModuleTypes)
        {
            _dependencyGraph[moduleType] = new List<Type>();
        }

        // 构建程序集到模块的映射
        var assemblyToModule = allModuleTypes.ToDictionary(
            m => m.Assembly,
            m => m
        );

        // 遍历每个模块，检查其程序集引用
        foreach (var moduleType in allModuleTypes)
        {
            var assembly = moduleType.Assembly;

            foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
            {
                try
                {
                    var referencedAssembly = Assembly.Load(referencedAssemblyName);

                    // 如果被引用的程序集也有模块，建立隐式依赖
                    if (assemblyToModule.TryGetValue(referencedAssembly, out var referencedModule))
                    {
                        if (!_dependencyGraph[moduleType].Contains(referencedModule))
                        {
                            _dependencyGraph[moduleType].Add(referencedModule);
                        }
                    }
                }
                catch (Exception)
                {
                    // 忽略无法加载的程序集
                }
            }
        }
    }

    /// <summary>
    /// 添加显式依赖（通过 DependsOn 声明）
    /// </summary>
    private void AddExplicitDependencies(List<Type> allModuleTypes)
    {
        foreach (var moduleType in allModuleTypes)
        {
            var moduleAttr = moduleType.GetCustomAttribute<ModuleAttribute>();
            if (moduleAttr?.DependsOn != null)
            {
                foreach (var dependency in moduleAttr.DependsOn)
                {
                    if (!_dependencyGraph[moduleType].Contains(dependency))
                    {
                        _dependencyGraph[moduleType].Add(dependency);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 拓扑排序：先按 Level/Order 分组，再按依赖关系排序
    /// </summary>
    private List<Type> TopologicalSort(List<Type> allModuleTypes)
    {
        // 按 Level 和 Order 排序
        var sortedByLevel = allModuleTypes
            .Select(t => new
            {
                Type = t,
                Attr = t.GetCustomAttribute<ModuleAttribute>() ?? new ModuleAttribute()
            })
            .OrderBy(x => (int)x.Attr.Level)
            .ThenBy(x => x.Attr.Order)
            .Select(x => x.Type)
            .ToList();

        // 在同 Level 内进行拓扑排序
        var result = new List<Type>();
        var visited = new HashSet<Type>();
        var visiting = new HashSet<Type>();

        foreach (var moduleType in sortedByLevel)
        {
            TopologicalSortVisit(moduleType, result, visited, visiting);
        }

        return result;
    }

    private void TopologicalSortVisit(
        Type moduleType,
        List<Type> result,
        HashSet<Type> visited,
        HashSet<Type> visiting)
    {
        if (visited.Contains(moduleType))
            return;

        if (visiting.Contains(moduleType))
        {
            // 检测到循环依赖
            throw new CircularDependencyException(visiting.Append(moduleType).ToArray());
        }

        visiting.Add(moduleType);

        // 先访问依赖
        if (_dependencyGraph.TryGetValue(moduleType, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                TopologicalSortVisit(dependency, result, visited, visiting);
            }
        }

        visiting.Remove(moduleType);
        visited.Add(moduleType);
        result.Add(moduleType);
    }

    /// <summary>
    /// 实例化所有模块
    /// </summary>
    private List<ArtisanModule> InstantiateModules(
        List<Type> sortedModuleTypes,
        IServiceCollection services,
        IConfiguration configuration)
    {
        var modules = new List<ArtisanModule>();
        var tempServiceProvider = BuildTempServiceProvider(services, configuration);

        foreach (var moduleType in sortedModuleTypes)
        {
            var module = (ArtisanModule)ActivatorUtilities.CreateInstance(tempServiceProvider, moduleType);
            modules.Add(module);
        }

        return modules;
    }

    private IServiceProvider BuildTempServiceProvider(IServiceCollection services, IConfiguration configuration)
    {
        var tempServices = new ServiceCollection();
        tempServices.AddSingleton(configuration);
        RegisterAppSettings(tempServices, configuration);
        return tempServices.BuildServiceProvider();
    }

    private void RegisterAppSettings(IServiceCollection services, IConfiguration configuration)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var appSettingAttr = type.GetCustomAttribute<AppSettingAttribute>();
                    if (appSettingAttr != null)
                    {
                        var section = configuration.GetSection(appSettingAttr.Section);
                        var instance = section.Get(type) ?? Activator.CreateInstance(type);
                        services.AddSingleton(type, instance!);
                    }
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // 忽略无法加载的程序集
            }
        }
    }

    /// <summary>
    /// 判断是否为系统程序集（跳过扫描）
    /// </summary>
    private static bool IsSystemAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return true;

        return assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase);
    }
}
```

### 4.8 Session 生命周期实现

Session 生命周期与 HTTP Session 绑定，当 HTTP Session 销毁时自动清理：

```csharp
namespace Artisan.Core.Session;

/// <summary>
/// Session 作用域服务工厂
/// 生命周期与 HTTP Session 绑定
/// </summary>
public interface ISessionServiceFactory
{
    T GetOrCreate<T>(string sessionId, Func<IServiceProvider, T> factory, IServiceProvider sp) where T : class;
    void OnSessionEnd(string sessionId);
}

/// <summary>
/// Session 生命周期实现
/// </summary>
public class SessionServiceFactory : ISessionServiceFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, SessionServiceBag> _sessionServices = new();

    private class SessionServiceBag
    {
        public ConcurrentDictionary<Type, object> Services { get; } = new();
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    }

    public T GetOrCreate<T>(string sessionId, Func<IServiceProvider, T> factory, IServiceProvider sp) where T : class
    {
        var bag = _sessionServices.GetOrAdd(sessionId, _ => new SessionServiceBag());
        bag.LastAccess = DateTime.UtcNow;
        return (T)bag.Services.GetOrAdd(typeof(T), _ => factory(sp));
    }

    /// <summary>
    /// HTTP Session 结束时调用，清理该 Session 的所有服务
    /// </summary>
    public void OnSessionEnd(string sessionId)
    {
        if (_sessionServices.TryRemove(sessionId, out var bag))
        {
            DisposeServices(bag.Services.Values);
        }
    }

    private void DisposeServices(IEnumerable<object> services)
    {
        foreach (var service in services)
        {
            if (service is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else if (service is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public void Dispose()
    {
        foreach (var bag in _sessionServices.Values)
        {
            DisposeServices(bag.Services.Values);
        }
        _sessionServices.Clear();
    }
}

/// <summary>
/// Session 生命周期中间件
/// 监听 HTTP Session 销毁事件
/// </summary>
public class SessionLifetimeMiddleware : IMiddleware
{
    private readonly ISessionServiceFactory _sessionFactory;

    public SessionLifetimeMiddleware(ISessionServiceFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // 确保 Session 已启动
        await context.Session.LoadAsync();

        // 注册 Session 结束回调
        context.Response.OnCompleted(async () =>
        {
            // 检查 Session 是否被标记为清除或过期
            if (context.Session.IsAvailable && SessionIsEnding(context))
            {
                _sessionFactory.OnSessionEnd(context.Session.Id);
            }
        });

        await next(context);
    }

    private bool SessionIsEnding(HttpContext context)
    {
        // 检查是否调用了 Session.Clear() 或 SignOut
        return context.Items.ContainsKey("__SessionCleared")
            || context.Response.Headers.ContainsKey("Set-Cookie")
               && context.Response.Headers["Set-Cookie"].ToString().Contains("expires=Thu, 01 Jan 1970");
    }
}

/// <summary>
/// Session 服务解析器 - 用于注册到 DI
/// </summary>
internal static class SessionServiceRegistrar
{
    public static void RegisterSessionService(
        IServiceCollection services,
        Type serviceType,
        Type implementationType)
    {
        services.AddScoped(serviceType, sp =>
        {
            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            var sessionFactory = sp.GetRequiredService<ISessionServiceFactory>();

            var context = httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("Session-scoped services require an active HTTP context");

            var sessionId = context.Session.Id;

            return sessionFactory.GetOrCreate(
                sessionId,
                serviceProvider => ActivatorUtilities.CreateInstance(serviceProvider, implementationType),
                sp
            );
        });
    }
}
```

**使用示例：**
```csharp
// 购物车组件 - 每个用户会话一个实例
[Component(SessionScoped = true)]
public class ShoppingCart : IShoppingCart, IDisposable
{
    private readonly List<CartItem> _items = new();

    public void AddItem(CartItem item) => _items.Add(item);
    public void RemoveItem(int productId) => _items.RemoveAll(i => i.ProductId == productId);
    public IReadOnlyList<CartItem> Items => _items;
    public decimal Total => _items.Sum(i => i.Price * i.Quantity);

    public void Dispose()
    {
        // 当 HTTP Session 销毁时自动调用
        Console.WriteLine("ShoppingCart disposed with session");
        _items.Clear();
    }
}

// 用户会话状态组件
[Component(SessionScoped = true)]
public class UserSessionState : IUserSessionState
{
    public string? CurrentTheme { get; set; }
    public string? Language { get; set; }
    public List<int> RecentlyViewed { get; } = new();
}

// 在 Controller 中使用（使用 .NET 原生构造函数注入）
[ApiController]
[Route("api/cart")]
public class CartController : ControllerBase
{
    private readonly IShoppingCart _cart;

    public CartController(IShoppingCart cart)
    {
        _cart = cart;
    }

    [HttpPost("add")]
    public IActionResult AddToCart([FromBody] CartItem item)
    {
        _cart.AddItem(item);  // 自动关联到当前用户的 Session
        return Ok(new { _cart.Total, Count = _cart.Items.Count });
    }
}
```

## 5. 潜在问题与解决方案

| 问题 | 描述 | 解决方案 |
|------|------|----------|
| **循环依赖** | A 依赖 B，B 依赖 A | 延迟初始化 + 启动时检测并报错 |
| **Session 内存泄漏** | Session 服务不释放 | 绑定 HTTP Session 销毁事件 |
| **配置热更新** | appsettings.json 变更后配置不更新 | IOptionsMonitor 支持 + 事件通知 |
| **程序集加载时机** | 动态加载的程序集无法扫描 | 提供 `ScanLoadedAssemblies()` API |
| **泛型服务注册** | `IRepository<T>` 开放泛型 | 支持 `[Injectable(typeof(IRepository<>))]` |

## 6. 实现步骤

### 步骤 1：创建解决方案结构
```
Artisan/
├── src/
│   └── Artisan/
└── tests/
    └── Artisan.Tests/
```

### 步骤 2：实现核心 Attributes
- 所有 Attribute 类定义

### 步骤 3：实现程序集扫描器
- Glob 模式匹配命名空间
- 类型发现与注册

### 步骤 4：实现依赖注入核心
- 属性注入器
- Keyed 服务支持
- 生命周期管理

### 步骤 5：实现模块系统
- ModuleLoader 模块加载器
- 循环依赖检测
- 拓扑排序

### 步骤 6：实现 Session 生命周期
- SessionServiceFactory
- HTTP Session 绑定

### 步骤 7：实现配置系统
- AppSetting 映射
- GetValue 注入

### 步骤 8：实现 ArtisanApplication 启动器
- 自动发现入口类（通过 [ArtisanApplication] 特性）
- 自动扫描应用命名空间
- 处理额外的 [ScanAssembly] 模式
- 启动流程

### 步骤 9：集成测试

## 7. 文件结构

```
ProCode-Server/
├── Artisan/
│   ├── src/
│   │   └── Artisan/
│   │       ├── Artisan.csproj
│   │       ├── Application/
│   │       │   ├── ArtisanApplication.cs
│   │       │   ├── IConfigurableApplication.cs
│   │       │   └── ArtisanOptions.cs
│   │       ├── Attributes/
│   │       │   ├── ArtisanApplicationAttribute.cs
│   │       │   ├── ScanAssemblyAttribute.cs
│   │       │   ├── InjectAttribute.cs
│   │       │   ├── InjectableAttribute.cs
│   │       │   ├── ServiceAttribute.cs
│   │       │   ├── RepositoryAttribute.cs
│   │       │   ├── ComponentAttribute.cs
│   │       │   └── DynamicInjectableAttribute.cs
│   │       ├── Modules/
│   │       │   ├── ModuleAttribute.cs
│   │       │   ├── ModuleLevel.cs
│   │       │   ├── ArtisanModule.cs
│   │       │   ├── ModuleLoader.cs
│   │       │   └── CircularDependencyException.cs
│   │       ├── Configuration/
│   │       │   ├── AppSettingAttribute.cs
│   │       │   ├── GetValueAttribute.cs
│   │       │   ├── IAppSettings.cs
│   │       │   ├── AppSettings.cs
│   │       │   └── ConfigurationRegistrar.cs
│   │       ├── DependencyInjection/
│   │       │   ├── Lifetime.cs
│   │       │   ├── IAssemblyScanner.cs
│   │       │   ├── AssemblyScanner.cs
│   │       │   ├── GlobMatcher.cs
│   │       │   ├── IPropertyInjector.cs
│   │       │   ├── PropertyInjector.cs
│   │       │   ├── ServiceRegistrar.cs
│   │       │   └── ArtisanServiceCollectionExtensions.cs
│   │       └── Session/
│   │           ├── ISessionServiceFactory.cs
│   │           ├── SessionServiceFactory.cs
│   │           └── SessionLifetimeMiddleware.cs
│   └── tests/
│       └── Artisan.Tests/
│           ├── Artisan.Tests.csproj
│           ├── ScannerTests.cs
│           ├── InjectionTests.cs
│           └── ModuleTests.cs
└── docs/
    ├── feature-dependency-injection.md
    └── feature-aop.md  # AOP 设计（后续迭代）
```

## 8. 完整使用示例

```csharp
// ========== MyApplication.cs ==========
namespace MyApp;

[ArtisanApplication]  // 自动扫描 MyApp.** 命名空间
public class MyApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);  // 无需指定启动模块，自动发现！
    }
}

// 不需要 AppModule！框架自动从程序集引用发现所有模块

// ========== appsettings.json ==========
{
  "App": {
    "Name": "MyApp",
    "Version": "1.0.0"
  },
  "Database": {
    "ConnectionString": "Server=localhost;Database=mydb",
    "MaxPoolSize": 100
  },
  "Cache": {
    "Type": "Redis",
    "RedisConnection": "localhost:6379"
  }
}

// ========== DatabaseConfig.cs ==========
[AppSetting("Database")]
public class DatabaseConfig
{
    public string ConnectionString { get; set; } = "";
    public int MaxPoolSize { get; set; } = 10;
}

// ========== CacheConfig.cs ==========
[AppSetting("Cache")]
public class CacheConfig
{
    public string Type { get; set; } = "Memory";
    public string RedisConnection { get; set; } = "";

    [DynamicInjectable("redis")]
    public ICache CreateRedisCache()
    {
        return new RedisCache(RedisConnection);
    }

    [DynamicInjectable("memory")]
    public ICache CreateMemoryCache()
    {
        return new MemoryCache();
    }
}

// ========== IUserService.cs ==========
public interface IUserService
{
    User? GetUser(int id);
    void CreateUser(User user);
}

// ========== UserService.cs ==========
[Service]
public class UserService : IUserService
{
    [Inject] private IUserRepository Repository { get; set; } = null!;
    [Inject("redis")] private ICache Cache { get; set; } = null!;
    [GetValue("App:Name")] private string AppName { get; set; } = "";

    public User? GetUser(int id)
    {
        return Repository.FindById(id);
    }

    public void CreateUser(User user)
    {
        Repository.Save(user);
    }
}

// ========== UserRepository.cs ==========
[Repository]
public class UserRepository : IUserRepository
{
    [AppSetting("Database")]
    private DatabaseConfig DbConfig { get; set; } = null!;

    public User? FindById(int id) { /* ... */ }
    public void Save(User user) { /* ... */ }
}

// ========== UserController.cs ==========
// Controller 使用 .NET 原生依赖注入（构造函数注入）
[ApiController]
[Route("api/users")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("{id}")]
    public ActionResult<User> Get(int id)
    {
        var user = _userService.GetUser(id);
        return user == null ? NotFound() : Ok(user);
    }
}

// ========== ShoppingCart.cs ==========
[Component(SessionScoped = true)]  // 每个用户会话一个购物车实例
public class ShoppingCart : IShoppingCart
{
    private readonly List<CartItem> _items = new();

    public void AddItem(CartItem item) => _items.Add(item);
    public IReadOnlyList<CartItem> Items => _items;
}
```

**用户的 .csproj - 所引即所得：**
```xml
<ItemGroup>
    <!-- 引用了这些包，对应的模块就会自动加载并排好序 -->
    <PackageReference Include="Artisan.WebApi" Version="1.0.0" />
    <PackageReference Include="Artisan.OpenApi" Version="1.0.0" />
    <PackageReference Include="Artisan.Redis" Version="1.0.0" />
    <PackageReference Include="Artisan.Auth" Version="1.0.0" />
    <ProjectReference Include="..\MyProject.User\MyProject.User.csproj" />
    <ProjectReference Include="..\MyProject.Order\MyProject.Order.csproj" />
</ItemGroup>
```

**自动推导的加载顺序：**
1. `SessionModule` (Infrastructure, Order=10)
2. `AuthModule` (Infrastructure, Order=20)
3. `RedisModule` (Infrastructure, Order=0)
4. `WebApiModule` (Infrastructure, Order=0)
5. `OpenApiModule` (Infrastructure, Order=0)
6. `UserModule` (Application)
7. `OrderModule` (Application)

---

**请确认设计方案或提出修改意见，确认后我将开始实施。**
