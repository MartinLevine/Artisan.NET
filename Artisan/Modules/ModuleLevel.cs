namespace Artisan.Modules;

/// <summary>
/// 模块层级 - 用于自动排序，无需显式声明依赖
/// </summary>
public enum ModuleLevel
{
    /// <summary>
    /// 核心底座 (Logger, EventBus)
    /// </summary>
    Kernel = 0,

    /// <summary>
    /// 基础设施 (Redis, Database, Session, Auth)
    /// </summary>
    Infrastructure = 10,

    /// <summary>
    /// 业务模块 (User, Order)
    /// </summary>
    Application = 20,

    /// <summary>
    /// 顶层入口（一般不需要，框架自动处理）
    /// </summary>
    Presentation = 100
}
