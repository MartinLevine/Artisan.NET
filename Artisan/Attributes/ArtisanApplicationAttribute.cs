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
