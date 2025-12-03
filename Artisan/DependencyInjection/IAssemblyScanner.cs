using System.Reflection;

namespace Artisan.DependencyInjection;

/// <summary>
/// 程序集扫描器接口
/// </summary>
public interface IAssemblyScanner
{
    /// <summary>
    /// 获取所有扫描到的程序集
    /// </summary>
    IReadOnlyList<Assembly> ScannedAssemblies { get; }

    /// <summary>
    /// 获取所有扫描到的类型
    /// </summary>
    IReadOnlyList<Type> ScannedTypes { get; }

    /// <summary>
    /// 扫描入口程序集及其引用的程序集
    /// </summary>
    /// <param name="entryAssembly">入口程序集</param>
    /// <param name="additionalPatterns">额外的命名空间匹配模式</param>
    void Scan(Assembly entryAssembly, IEnumerable<string>? additionalPatterns = null);

    /// <summary>
    /// 获取所有带有指定 Attribute 的类型
    /// </summary>
    /// <typeparam name="TAttribute">Attribute 类型</typeparam>
    /// <returns>类型列表</returns>
    IEnumerable<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute;

    /// <summary>
    /// 获取所有实现指定接口或继承指定基类的类型
    /// </summary>
    /// <typeparam name="TBase">基类或接口类型</typeparam>
    /// <returns>类型列表</returns>
    IEnumerable<Type> GetTypesAssignableTo<TBase>();

    /// <summary>
    /// 获取所有实现指定接口或继承指定基类的类型
    /// </summary>
    /// <param name="baseType">基类或接口类型</param>
    /// <returns>类型列表</returns>
    IEnumerable<Type> GetTypesAssignableTo(Type baseType);
}
