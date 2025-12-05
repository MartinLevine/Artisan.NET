using System.Text;
using Microsoft.Extensions.Logging;

namespace Artisan.Extensions;

public static class LoggerExtensions
{
    /// <summary>
    /// 以表格形式打印对象集合
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="logger">Logger 实例</param>
    /// <param name="source">数据源</param>
    /// <param name="configureColumns">列配置委托</param>
    /// <param name="level">日志级别 (默认 Information)</param>
    public static void LogTable<T>(
        this ILogger logger,
        IEnumerable<T> source,
        Action<TableColumnBuilder<T>> configureColumns,
        LogLevel level = LogLevel.Information)
    {
        if (source == null || !source.Any()) return;

        // 1. 构建列定义
        var builder = new TableColumnBuilder<T>();
        configureColumns(builder);
        var columns = builder.Columns;

        if (columns.Count == 0) return;

        // 为了避免多次枚举 source (如果是 LINQ 查询)，先转为 List
        var rows = source.ToList();

        // 2. 预计算每一列的字符串值，并计算最大宽度
        // 结构: List<Row<ColValue>>
        var rowValues = new List<string[]>(rows.Count);
        var colWidths = new int[columns.Count];

        // 初始化宽度为表头长度
        for (int i = 0; i < columns.Count; i++)
        {
            colWidths[i] = columns[i].Header.Length;
        }

        foreach (var item in rows)
        {
            var values = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                // 获取值并转为字符串，处理 null
                var val = columns[i].ValueSelector(item)?.ToString() ?? "";
                values[i] = val;

                // 更新该列最大宽度
                if (val.Length > colWidths[i])
                {
                    colWidths[i] = val.Length;
                }
            }

            rowValues.Add(values);
        }

        // 3. 准备格式化字符串
        // 为了美观，列宽多加 2 个空格 padding
        for (int i = 0; i < colWidths.Length; i++) colWidths[i] += 2;

        var separatorLine = "+" + string.Join("+", colWidths.Select(w => new string('-', w))) + "+";

        // 生成行格式，例如: "| {0,-10}| {1,-20}|..."
        var rowFormatBuilder = new StringBuilder("|");
        for (int i = 0; i < columns.Count; i++)
        {
            rowFormatBuilder.Append($" {{{i},-{colWidths[i]}}}|");
        }

        var rowFormat = rowFormatBuilder.ToString();

        // 4. 开始打印
        // 打印表头
        logger.Log(level, separatorLine);

        var headerValues = columns.Select(c => c.Header).ToArray();
        logger.Log(level, string.Format(rowFormat, headerValues));

        logger.Log(level, separatorLine);

        // 打印数据
        foreach (var row in rowValues)
        {
            logger.Log(level, string.Format(rowFormat, row));
        }

        logger.Log(level, separatorLine);
    }

    // 辅助类：用于构建列定义
    public class TableColumnBuilder<T>
    {
        internal List<TableColumn<T>> Columns { get; } = new();

        /// <summary>
        /// 添加一列
        /// </summary>
        /// <param name="header">表头名称</param>
        /// <param name="selector">如何从对象中获取值</param>
        public TableColumnBuilder<T> AddColumn(string header, Func<T, object?> selector)
        {
            Columns.Add(new TableColumn<T>(header, selector));
            return this;
        }
    }

    internal record TableColumn<T>(string Header, Func<T, object?> ValueSelector);
}