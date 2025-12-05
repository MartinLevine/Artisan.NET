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
        
        // 1. 计算每列内容的“纯净”最大宽度 (不含 padding)
        var contentWidths = new int[columns.Count];

        // 先初始化为表头长度
        for (int i = 0; i < columns.Count; i++)
        {
            contentWidths[i] = columns[i].Header.Length;
        }

        // 遍历所有数据，更新最大宽度
        foreach (var item in rows)
        {
            var values = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var val = columns[i].ValueSelector(item)?.ToString() ?? "";
                values[i] = val;
                
                if (val.Length > contentWidths[i])
                {
                    contentWidths[i] = val.Length;
                }
            }
            rowValues.Add(values);
        }

        // 2. 生成分割线
        // 规则：分隔线长度 = 内容宽度 + 2 (左边1空格 + 右边1空格)
        // 样式：+--------+-------+
        var separatorBuilder = new StringBuilder("+");
        foreach (var width in contentWidths)
        {
            separatorBuilder.Append(new string('-', width + 2)).Append('+');
        }
        var separatorLine = separatorBuilder.ToString();

        // 3. 生成行格式字符串
        // 规则：| {0,-Width} | (注意大括号内外的空格)
        // 样式：| Method | Route |
        var rowFormatBuilder = new StringBuilder("|");
        for (int i = 0; i < columns.Count; i++)
        {
            // {i, -width} 表示：参数i，左对齐，占width位
            // 我们在占位符前后各手动加一个空格
            rowFormatBuilder.Append($" {{{i},-{contentWidths[i]}}} |");
        }
        var rowFormat = rowFormatBuilder.ToString();

        // 4. 打印
        logger.Log(level, separatorLine);
        
        // 表头
        var headerValues = columns.Select(c => c.Header).ToArray();
        logger.Log(level, string.Format(rowFormat, headerValues));
        
        logger.Log(level, separatorLine);

        // 数据行
        foreach (var row in rowValues)
        {
            logger.Log(level, string.Format(rowFormat, row));
        }

        logger.Log(level, separatorLine);
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