using Artisan.Modules;

namespace Artisan;

/// <summary>
/// Artisan 框架配置选项
/// </summary>
public class ArtisanOptions
{
    // 被禁用的模块类型集合
    private readonly HashSet<Type> _disabledModules = [];

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

    /// <summary>
    /// 检查模块是否被禁用
    /// </summary>
    public bool IsModuleDisabled(Type moduleType) => _disabledModules.Contains(moduleType);

    /// <summary>
    /// 获取模块替换类型（如果有）
    /// </summary>
    public Type? GetModuleReplacement(Type originalType) =>
        _moduleReplacements.TryGetValue(originalType, out var replacement) ? replacement : null;

    /// <summary>
    /// 获取所有被禁用的模块类型
    /// </summary>
    public IReadOnlyCollection<Type> DisabledModules => _disabledModules;

    /// <summary>
    /// 获取所有模块替换映射
    /// </summary>
    public IReadOnlyDictionary<Type, Type> ModuleReplacements => _moduleReplacements;
}