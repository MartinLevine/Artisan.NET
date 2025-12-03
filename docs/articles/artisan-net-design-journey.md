# Artisan.NET 框架设计之旅：从样板代码到零配置体验

> 本文记录了 Artisan.NET 依赖注入框架的设计演进过程，探讨如何在 .NET 生态中实现 Spring Boot 式的"零配置"开发体验。

## 1. 问题的起源

每个 .NET 开发者都写过这样的代码：

```csharp
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

这段代码在每个项目中几乎一模一样。当项目增多时，维护这些样板代码成为负担。更糟糕的是，当需要添加 Redis、认证、日志等功能时，`Program.cs` 会迅速膨胀。

**我们的目标**：设计一个框架，让开发者只需要写业务代码，框架自动处理所有基础设施配置。

## 2. 第一版设计：显式模块依赖

### 2.1 最初的想法

借鉴 ABP Framework 的模块化思想，我们设计了显式依赖声明：

```csharp
[Module(DependsOn = [typeof(WebApiModule), typeof(RedisModule), typeof(AuthModule)])]
public class AppModule : ArtisanModule { }
```

用户需要：
1. 创建一个 `AppModule` 作为入口
2. 通过 `DependsOn` 显式声明所有依赖的模块
3. 调用 `ArtisanApplication.Run<AppModule>(args)` 启动

### 2.2 问题浮现

这种设计虽然清晰，但存在明显问题：

1. **冗余声明**：用户已经在 `.csproj` 中引用了 `Artisan.Redis` 包，为什么还要在代码中再写一次 `DependsOn = [typeof(RedisModule)]`？

2. **维护负担**：每添加一个新模块，都要修改 `DependsOn` 列表。

3. **容易遗漏**：忘记添加依赖会导致运行时错误。

## 3. 核心洞察：所引即所得

### 3.1 思维转变

关键洞察来自对 Spring Boot 的观察：

> **物理依赖 = 逻辑依赖**

如果 `MyApp.dll` 引用了 `Artisan.Redis.dll`，这个物理引用关系本身就说明了依赖关系。我们不需要用户再声明一次。

这就是"**所引即所得**"的核心理念：
- 用户在 `.csproj` 中添加包引用
- 框架自动发现并加载对应模块
- 无需任何额外配置

### 3.2 程序集引用推导

.NET 的反射 API 提供了 `Assembly.GetReferencedAssemblies()` 方法，可以获取程序集引用的所有其他程序集。利用这个能力：

```csharp
// 从入口程序集开始
var entryAssembly = Assembly.GetEntryAssembly();

// 递归扫描所有引用的程序集
foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
{
    var referencedAssembly = Assembly.Load(referencedAssemblyName);
    // 在每个程序集中查找 ArtisanModule 子类
    var moduleType = FindModuleInAssembly(referencedAssembly);
}
```

### 3.3 新的用户体验

用户的代码变得极其简洁：

```csharp
[ArtisanApplication]
public class MyApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);  // 无需指定启动模块！
    }
}
```

用户的 `.csproj`：

```xml
<ItemGroup>
    <!-- 引用了这些包，对应的模块就会自动加载 -->
    <PackageReference Include="Artisan.Redis" Version="1.0.0" />
    <PackageReference Include="Artisan.OpenApi" Version="1.0.0" />
    <PackageReference Include="Artisan.Auth" Version="1.0.0" />
</ItemGroup>
```

**这就是全部配置**。框架会自动发现 `RedisModule`、`OpenApiModule`、`AuthModule` 并按正确顺序加载。

## 4. 新问题：模块加载顺序

### 4.1 中间件顺序的重要性

自动发现解决了"加载什么"的问题，但引入了新问题："按什么顺序加载"？

在 ASP.NET Core 中，中间件顺序至关重要：

```csharp
// 正确顺序
app.UseSession();        // Session 必须先于 Auth
app.UseAuthentication();
app.UseAuthorization();

// 错误顺序会导致认证失败
app.UseAuthentication(); // Auth 依赖 Session，但 Session 还没初始化！
app.UseSession();
```

### 4.2 解决方案：ModuleLevel + Order

我们引入了层级化的模块排序机制：

```csharp
public enum ModuleLevel
{
    Kernel = 0,          // 核心底座 (Logger, EventBus)
    Infrastructure = 10, // 基础设施 (Redis, Database, Session, Auth)
    Application = 20,    // 业务模块 (User, Order)
    Presentation = 100   // 顶层入口
}
```

框架模块使用 `Level` 和 `Order` 声明自己的位置：

```csharp
// Session 必须在 Auth 之前
[Module(Level = ModuleLevel.Infrastructure, Order = 10)]
public class SessionModule : ArtisanModule { }

[Module(Level = ModuleLevel.Infrastructure, Order = 20)]
public class AuthModule : ArtisanModule { }
```

### 4.3 排序算法

最终的排序策略：

1. **先按 Level 排序**：Kernel → Infrastructure → Application → Presentation
2. **同 Level 按 Order 排序**：数值小的先加载
3. **最后按依赖关系拓扑排序**：确保被依赖的模块先加载

```csharp
var sortedModules = allModuleTypes
    .OrderBy(m => m.GetAttribute<ModuleAttribute>().Level)
    .ThenBy(m => m.GetAttribute<ModuleAttribute>().Order)
    .ToList();

// 然后进行拓扑排序，处理隐式依赖
TopologicalSort(sortedModules);
```

## 5. 用户定制能力

### 5.1 问题：用户需要控制权

零配置很好，但用户有时需要：
- 禁用某个自动加载的模块
- 用自定义实现替换框架默认模块
- 在模块加载前后执行自定义逻辑

### 5.2 解决方案：IConfigurableApplication

我们设计了三阶段配置接口：

```csharp
public interface IConfigurableApplication
{
    // 阶段 1：框架预配置（最先执行）
    void ConfigureArtisan(ArtisanOptions options) { }

    // 阶段 2：服务注册（中间执行）
    void ConfigureServices(IServiceCollection services) { }

    // 阶段 3：中间件管道（最后执行）
    void Configure(WebApplication app) { }
}
```

用户可以选择性实现：

```csharp
[ArtisanApplication]
public class MyApplication : IConfigurableApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);
    }

    public void ConfigureArtisan(ArtisanOptions options)
    {
        // 禁用框架自带的 OpenApi 模块
        options.DisableModule<OpenApiModule>();

        // 用自定义模块替换默认模块
        options.ReplaceModule<RedisModule, MyCustomRedisModule>();
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // 注册应用级别的服务
        services.AddSingleton<IMyService, MyService>();
    }

    public void Configure(WebApplication app)
    {
        // 添加自定义端点
        app.MapGet("/health", () => "OK");
    }
}
```

### 5.3 设计要点

- **接口方法有默认实现**：用户只需实现需要的方法
- **阶段顺序清晰**：预配置 → 服务注册 → 中间件
- **用户代码最后执行**：可以覆盖模块的默认配置

## 6. 配置系统的演进

### 6.1 最初的简单设计

```csharp
[AppSetting("Database")]
public class DatabaseConfig
{
    public string ConnectionString { get; set; }
}
```

### 6.2 问题：嵌套对象和热更新

实际项目中，配置往往是嵌套的：

```json
{
  "Database": {
    "ConnectionString": "...",
    "Retry": {
      "MaxAttempts": 3,
      "DelayMs": 1000
    }
  }
}
```

而且，生产环境需要配置热更新能力——修改配置文件后无需重启应用。

### 6.3 解决方案：复用 IOptions<T>

不重新发明轮子，直接复用 .NET 的 Options 模式：

```csharp
// 框架内部实现
services.Configure<DatabaseConfig>(configuration.GetSection("Database"));

// 用户可以使用标准的 IOptions<T> 或 IOptionsMonitor<T>
public class DataService
{
    public DataService(IOptionsMonitor<DatabaseConfig> options)
    {
        options.OnChange(config =>
            Console.WriteLine($"Config updated: {config.ConnectionString}"));
    }
}
```

这样：
- 嵌套对象自动绑定（.NET 原生支持）
- 热更新开箱即用（`IOptionsMonitor<T>`）
- 与 .NET 生态完全兼容

## 7. 设计原则总结

### 7.1 约定优于配置

- 默认行为覆盖 80% 的场景
- 需要定制时才显式配置
- 零配置是目标，但不牺牲灵活性

### 7.2 不重新发明轮子

- `IOptions<T>` 用于配置绑定
- `IServiceCollection` 用于依赖注入
- `WebApplication` 用于应用托管

Artisan.NET 是 .NET 的封装层，不是替代品。

### 7.3 渐进式复杂度

用户按需引入复杂度：

| 场景 | 用户代码量 |
|------|-----------|
| 最简应用 | 5 行（Main + Attribute） |
| 禁用模块 | +10 行（实现 ConfigureArtisan） |
| 自定义服务 | +10 行（实现 ConfigureServices） |
| 完全控制 | 实现完整接口 |

### 7.4 物理结构反映逻辑结构

- 程序集引用 = 模块依赖
- 命名空间 = 扫描范围
- 文件结构 = 功能分组

## 8. 最终架构

```
┌─────────────────────────────────────────────────────────────┐
│                    用户应用 (MyApp)                          │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ [ArtisanApplication]                                 │   │
│  │ public class MyApplication : IConfigurableApplication│   │
│  │ {                                                    │   │
│  │     ArtisanApplication.Run(args);                   │   │
│  │ }                                                    │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                 Artisan.NET 框架核心                         │
│                                                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │ 程序集扫描器  │  │ 模块加载器   │  │ 属性注入器   │      │
│  │              │  │              │  │              │      │
│  │ - Glob 匹配  │  │ - 自动发现   │  │ - [Inject]   │      │
│  │ - 类型发现   │  │ - 依赖推导   │  │ - [AppSetting]│      │
│  │              │  │ - 拓扑排序   │  │              │      │
│  └──────────────┘  └──────────────┘  └──────────────┘      │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    .NET 原生基础设施                         │
│                                                             │
│  IServiceCollection  │  IOptions<T>  │  WebApplication     │
└─────────────────────────────────────────────────────────────┘
```

## 9. 经验与反思

### 9.1 好的设计是迭代出来的

- 第一版：显式依赖声明 → 太繁琐
- 第二版：自动发现 → 顺序问题
- 第三版：Level + Order → 缺少定制能力
- 最终版：IConfigurableApplication → 平衡自动化与控制

### 9.2 向成熟框架学习

- Spring Boot 的自动配置思想
- ABP 的模块化架构
- .NET 自身的 Options 模式

### 9.3 保持简单

最好的代码是不需要写的代码。Artisan.NET 的目标不是提供更多功能，而是让用户写更少的代码。

---

## 附录：核心 API 速览

```csharp
// 最简应用
[ArtisanApplication]
public class MyApplication
{
    public static void Main(string[] args) => ArtisanApplication.Run(args);
}

// 服务定义
[Service]
public class UserService : IUserService
{
    [Inject] private IUserRepository Repository { get; set; }
    [GetValue("App:Name")] private string AppName { get; set; }
}

// 模块定义（框架开发者使用）
[Module(Level = ModuleLevel.Infrastructure, Order = 10)]
public class RedisModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddStackExchangeRedisCache(...);
    }
}

// 配置类
[AppSetting("Database")]
public class DatabaseConfig
{
    public string ConnectionString { get; set; }
    public RetryConfig Retry { get; set; }  // 支持嵌套
}
```

---

*本文基于 Artisan.NET 框架的实际设计过程整理，记录了从问题发现到方案落地的完整思考路径。*
