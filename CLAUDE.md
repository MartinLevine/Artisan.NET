# ProCode-Server 项目指南

## 项目概述

ProCode-Server 是一个基于 .NET 10 的后端服务项目，核心包含 **Artisan.NET** 框架 —— 一个类似 Spring Boot 的依赖注入框架，旨在提供"极简、快速接入、最少样板代码、所引即所得"的开发体验。

## 项目结构

```
ProCode-Server/
├── Artisan/                    # Artisan.NET 核心框架
│   ├── Application/            # 应用启动器
│   ├── Attributes/             # 所有 Attributes
│   ├── Configuration/          # 配置系统
│   ├── DependencyInjection/    # DI 核心
│   ├── Modules/                # 模块系统
│   └── Session/                # Session 生命周期
├── api-gateway/                # API 网关服务
├── docs/                       # 设计文档
│   ├── feature-dependency-injection.md  # DI 框架设计文档
│   └── feature-aop.md                   # AOP 设计文档（待实现）
└── compose.yaml                # Docker Compose 配置
```

## 技术栈

- **.NET 10** (Preview)
- **ASP.NET Core** Web API
- **Docker** 容器化支持

## 核心概念

### Artisan.NET 框架

核心理念："所引即所得" —— 用户只需在 `.csproj` 中引用 NuGet 包或项目，框架自动发现并加载对应模块。

#### 主要 Attributes

| Attribute | 用途 | 默认生命周期 |
|-----------|------|-------------|
| `[ArtisanApplication]` | 应用入口 | - |
| `[Service]` | 服务层组件 | Scoped |
| `[Repository]` | 数据访问层 | Scoped |
| `[Component]` | 通用组件 | Singleton |
| `[Inject]` | 属性/字段注入 | - |
| `[Module]` | 模块配置 | - |
| `[AppSetting]` | 配置类映射 | - |

#### 模块层级

```csharp
public enum ModuleLevel
{
    Kernel = 0,          // 核心底座
    Infrastructure = 10, // 基础设施
    Application = 20,    // 业务模块
    Presentation = 100   // 顶层入口
}
```

## 常用命令

### 构建项目
```bash
dotnet build
```

### 运行 API Gateway
```bash
dotnet run --project api-gateway
```

### 运行测试
```bash
dotnet test
```

### Docker 构建
```bash
docker compose build
```

### Docker 运行
```bash
docker compose up
```

## 开发规范

### 代码风格

- 使用 C# 12+ 特性
- 启用 nullable reference types
- 使用文件范围命名空间
- 优先使用 primary constructors

### 命名约定

- **接口**: `I` 前缀，如 `IUserService`
- **Attribute**: `Attribute` 后缀，如 `ServiceAttribute`
- **异常**: `Exception` 后缀，如 `CircularDependencyException`

### 依赖注入

- 优先使用构造函数注入
- 属性注入使用 `[Inject]` 标记
- 配置类使用 `[AppSetting]` 标记

## 设计文档

实现新功能前，请先：

1. 阅读相关设计文档（`docs/` 目录）
2. 如无现有设计，创建 `docs/feature-<功能名>.md`
3. 设计文档需包含：
   - 核心原理
   - 实现步骤
   - 需要修改/创建/删除的文件
   - 核心实现代码
4. 等待确认后再实施

## 注意事项

- 修改 Artisan 框架核心代码前，需充分理解现有设计
- 模块系统基于程序集引用自动推导依赖关系
- AOP 功能目前为后续迭代计划，暂未实现
- 确保新功能与现有 "所引即所得" 设计理念一致

## 技术选型规则

**重要**：涉及第三方库/NuGet 包选型决策时：
1. 不要自行决定使用哪个库
2. 在设计文档中列出候选方案
3. 等待用户调研完成后给出最终选择
4. 用户确认后再更新设计文档并实施
