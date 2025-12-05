using System.Text;
using Microsoft.Extensions.Logging;

namespace Artisan.Extensions;

public static class LoggerExtensions
{
    public static void LogTable<T>(
        this ILogger logger, 
        IEnumerable<T> source, 
        Action<TableColumnBuilder<T>> configureColumns,
        LogLevel level = LogLevel.Information)
    {
        if (source == null || !source.Any()) return;

        var builder = new TableColumnBuilder<T>();
        configureColumns(builder);
        var columns = builder.Columns;
        
        if (columns.Count == 0) return;

        var rows = source.ToList();
        var rowValues = new List<string[]>(rows.Count);
        
        // 1. 计算每列内容的“最大视觉宽度”
        var maxVisualWidths = new int[columns.Count];

        // 初始化为表头宽度
        for (int i = 0; i < columns.Count; i++)
        {
            maxVisualWidths[i] = GetVisualWidth(columns[i].Header);
        }

        // 遍历数据更新最大宽度
        foreach (var item in rows)
        {
            var values = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var val = columns[i].ValueSelector(item)?.ToString() ?? "";
                values[i] = val;
                
                var width = GetVisualWidth(val);
                if (width > maxVisualWidths[i])
                {
                    maxVisualWidths[i] = width;
                }
            }
            rowValues.Add(values);
        }

        // 2. 生成分割线
        var separatorBuilder = new StringBuilder("+");
        foreach (var width in maxVisualWidths)
        {
            // 内容宽 + 左右各1空格
            separatorBuilder.Append(new string('-', width + 2)).Append('+');
        }
        var separatorLine = separatorBuilder.ToString();

        // 3. 打印逻辑 (不能再用 string.Format 自动对齐了，必须手动拼接)
        
        // 打印顶部分割线
        logger.Log(level, separatorLine);
        
        // 打印表头
        var headerSb = new StringBuilder("|");
        for (int i = 0; i < columns.Count; i++)
        {
            headerSb.Append(" ");
            headerSb.Append(PadRightVisual(columns[i].Header, maxVisualWidths[i]));
            headerSb.Append(" |");
        }
        logger.Log(level, headerSb.ToString());
        
        // 打印中间分割线
        logger.Log(level, separatorLine);

        // 打印数据行
        foreach (var row in rowValues)
        {
            var rowSb = new StringBuilder("|");
            for (int i = 0; i < columns.Count; i++)
            {
                rowSb.Append(" ");
                rowSb.Append(PadRightVisual(row[i], maxVisualWidths[i]));
                rowSb.Append(" |");
            }
            logger.Log(level, rowSb.ToString());
        }

        // 打印底部分割线
        logger.Log(level, separatorLine);
    }

    /// <summary>
    /// 获取字符串的视觉宽度 (ASCII=1, 中文=2)
    /// </summary>
    private static int GetVisualWidth(string? str)
    {
        if (string.IsNullOrEmpty(str)) return 0;
        
        int length = 0;
        foreach (var c in str)
        {
            // 简单的判断：ASCII 字符 (0-127) 算 1，其他算 2 (包括中文、全角符号、Emoji等)
            // 这是一个工程近似值，对于控制台打印足够准确
            length += (c >= 0 && c <= 127) ? 1 : 2;
        }
        return length;
    }

    /// <summary>
    /// 基于视觉宽度进行右侧填充
    /// </summary>
    private static string PadRightVisual(string str, int totalWidth)
    {
        int currentWidth = GetVisualWidth(str);
        int paddingNeeded = totalWidth - currentWidth;
        
        if (paddingNeeded <= 0) return str;
        
        return str + new string(' ', paddingNeeded);
    }

    public class TableColumnBuilder<T>
    {
        internal List<TableColumn<T>> Columns { get; } = new();

        public TableColumnBuilder<T> AddColumn(string header, Func<T, object?> selector)
        {
            Columns.Add(new TableColumn<T>(header, selector));
            return this;
        }
    }

    internal record TableColumn<T>(string Header, Func<T, object?> ValueSelector);
}