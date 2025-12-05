# ProCode-Server 项目指南

## 项目概述

这是一个基于 .NET 10 的 Web 服务端项目，核心是 **Artisan** 框架 —— 一个类似 Spring Boot 的依赖注入框架，提供自动扫描、模块化架构和声明式服务注册能力。

## 技术栈

- **运行时**: .NET 10.0
- **框架**: ASP.NET Core + Artisan (自研 DI 框架)
- **IDE**: JetBrains Rider
- **解决方案格式**: .slnx (新格式)

## 项目结构

```
ProCode-Server/
├── Artisan/                    # 核心框架库
│   ├── Attributes/             # 特性标注
│   │   ├── ArtisanApplicationAttribute.cs   # 应用入口标记
│   │   ├── InjectableAttribute.cs           # 可注入组件基类
│   │   ├── ServiceAttribute.cs              # 服务层组件
│   │   ├── ConfigurationAttribute.cs        # 配置绑定
│   │   ├── ModuleAttribute.cs               # 模块标记
│   │   └── ScanAssemblyAttribute.cs         # 程序集扫描
│   ├── DependencyInjection/    # 依赖注入核心
│   │   ├── AssemblyScanner.cs               # 程序集扫描器
│   │   ├── ServiceRegistrar.cs              # 服务注册器
│   │   ├── Lifetime.cs                      # 生命周期枚举
│   │   └── AssemblyHelper.cs                # 程序集辅助
│   ├── Modules/                # 模块系统
│   │   ├── ArtisanModule.cs                 # 模块基类
│   │   ├── ModuleLoader.cs                  # 模块加载器
│   │   └── ModuleLevel.cs                   # 模块级别
│   ├── Configuration/          # 配置系统
│   │   └── ConfigRegistrar.cs               # 配置注册器
│   ├── ArtisanApplication.cs   # 应用启动入口
│   ├── ArtisanOptions.cs       # 框架配置选项
│   └── IConfigurableApplication.cs  # 可配置应用接口
├── ProCode.Hosting/            # 宿主应用 (示例项目)
│   ├── Application.cs          # 应用入口
│   ├── Controllers/            # API 控制器
│   └── Models/                 # 数据模型
├── api-gateway/                # API 网关 (预留)
└── docs/                       # 文档目录
```

## 构建与运行

```bash
# 构建项目
dotnet build

# 运行应用
dotnet run --project ProCode.Hosting

# 运行测试
dotnet test
```

## Artisan 框架核心概念

### 1. 应用入口

使用 `[ArtisanApplication]` 标记应用入口类：

```csharp
[ArtisanApplication]
public class Application : IConfigurableApplication
{
    public static void Main(string[] args)
    {
        ArtisanApplication.Run(args);
    }
}
```

### 2. 依赖注入

使用 `[Injectable]` 或 `[Service]` 等特性自动注册服务：

```csharp
[Service]  // 默认 Scoped 生命周期
public class UserService : IUserService
{
    // 自动注册为 IUserService 和 UserService
}

[Injectable(Lifetime = Lifetime.Singleton)]
public class CacheService
{
    // 单例服务
}
```

**生命周期选项**:
- `Transient` - 每次注入创建新实例
- `Scoped` - 每个请求一个实例
- `Singleton` - 全局单例

### 3. 模块系统

继承 `ArtisanModule` 创建功能模块：

```csharp
[Module]
public class MyModule : ArtisanModule
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册服务
    }

    public override void Configure(WebApplication app)
    {
        // 配置中间件
    }
}
```

### 4. 配置绑定

使用 `[Configuration]` 自动绑定配置节：

```csharp
[Configuration("Database")]
public class DatabaseConfig
{
    public string ConnectionString { get; set; }
    public int Timeout { get; set; }
}
```

### 5. 应用配置接口

实现 `IConfigurableApplication` 参与框架配置流程：

```csharp
public interface IConfigurableApplication
{
    void ConfigureArtisan(ArtisanOptions options);    // 阶段1: 框架预配置
    void ConfigureServices(IServiceCollection services); // 阶段2: 服务注册
    void Configure(WebApplication app);               // 阶段3: 中间件配置
}
```

## 启动流程

1. **PrepareEnvironment** - 找到入口类型，实例化用户 Application
2. **ScanAssemblies** - BFS 扫描所有相关程序集
3. **CreateBuilder** - 创建 WebApplicationBuilder
4. **RegisterServices** - 加载模块，注册服务和配置
5. **Build** - 构建 WebApplication
6. **ConfigurePipeline** - 配置中间件管道
7. **Run** - 启动应用

## 代码规范

- 使用 C# 最新语法特性 (file-scoped namespace, primary constructor 等)
- 启用 Nullable Reference Types
- 使用 Implicit Usings
- 遵循 .NET 命名约定

## 注意事项

- `api-gateway` 目录当前为预留模块，暂无实际代码
- 框架设计参考 Spring Boot，但保持 .NET 生态惯例
- 模块加载支持条件判断 (`ShouldLoad`) 和依赖声明 (`DependsOn`)
