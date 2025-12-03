using System.Text.RegularExpressions;

namespace Artisan.DependencyInjection;

/// <summary>
/// Glob 模式匹配器
/// 支持命名空间的通配符匹配
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
    /// *     - 匹配任意字符（不含点）
    /// **    - 匹配任意字符（含点）
    /// ?     - 匹配单个字符
    /// [abc] - 匹配字符集
    /// </remarks>
    public static bool IsMatch(string pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(name))
            return false;

        var regexPattern = ConvertToRegex(pattern);
        return Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 将 Glob 模式转换为正则表达式
    /// </summary>
    private static string ConvertToRegex(string pattern)
    {
        var regex = new System.Text.StringBuilder();
        regex.Append('^');

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            switch (c)
            {
                case '*':
                    // 检查是否是 **
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        regex.Append(".*"); // ** 匹配任意字符（含点）
                        i++; // 跳过下一个 *
                    }
                    else
                    {
                        regex.Append("[^.]*"); // * 匹配任意字符（不含点）
                    }
                    break;

                case '?':
                    regex.Append("[^.]"); // ? 匹配单个非点字符
                    break;

                case '[':
                    // 字符集直接保留
                    regex.Append('[');
                    break;

                case ']':
                    regex.Append(']');
                    break;

                case '.':
                    regex.Append(@"\.");
                    break;

                case '\\':
                case '^':
                case '$':
                case '|':
                case '+':
                case '(':
                case ')':
                case '{':
                case '}':
                    regex.Append('\\');
                    regex.Append(c);
                    break;

                default:
                    regex.Append(c);
                    break;
            }
        }

        regex.Append('$');
        return regex.ToString();
    }
}
