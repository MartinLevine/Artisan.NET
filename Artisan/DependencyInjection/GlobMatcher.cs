using Microsoft.Extensions.FileSystemGlobbing;

namespace Artisan.DependencyInjection;

/// <summary>
/// Glob 模式匹配器
/// 使用 Microsoft.Extensions.FileSystemGlobbing 库实现
/// </summary>
public static class GlobMatcher
{
    /// <summary>
    /// 检查名称是否匹配 Glob 模式
    /// </summary>
    /// <param name="pattern">Glob 模式</param>
    /// <param name="name">要匹配的名称</param>
    /// <returns>是否匹配</returns>
    /// <remarks>
    /// 支持的通配符：
    /// *     - 匹配任意字符（不含分隔符）
    /// **    - 匹配任意字符（含分隔符），可匹配零个或多个段
    /// ?     - 匹配单个字符
    /// [abc] - 匹配字符集
    /// </remarks>
    public static bool IsMatch(string pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(name))
            return false;

        try
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern);

            // FileSystemGlobbing 针对路径设计，将程序集名称转换为路径格式
            // 例如 "ProCode.Hosting" -> "ProCode/Hosting"
            var pathName = name.Replace(".", "/");
            return matcher.Match(pathName).HasMatches;
        }
        catch
        {
            return false;
        }
    }
}
