namespace Artisan.Modules;

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
