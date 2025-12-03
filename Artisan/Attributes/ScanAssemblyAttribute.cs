namespace Artisan.Attributes;

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
