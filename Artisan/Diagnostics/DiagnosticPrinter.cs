using System.Reflection;
using System.Text;
using Artisan.Attributes;
using Artisan.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Artisan.Diagnostics;

/// <summary>
/// 诊断输出工具
/// 用于在应用启动时输出框架诊断信息
/// </summary>
public static class DiagnosticPrinter
{
    /// <summary>
    /// 打印已注册的服务信息（以 ASCII 表格形式）
    /// </summary>
    public static void PrintRegisteredServices(IServiceCollection services)
    {
        var servicesByType = services
            .GroupBy(d => d.ServiceType)
            .OrderBy(g => g.Key.Name)
            .ToList();

        if (servicesByType.Count == 0)
        {
            PrintTable(new[] { "Service Type", "Lifetime", "Implementation" }, new List<string[]>());
            return;
        }

        var rows = new List<string[]>();
        foreach (var group in servicesByType)
        {
            var descriptor = group.First();
            var serviceName = FormatTypeName(descriptor.ServiceType);
            var lifetime = descriptor.Lifetime.ToString();
            var implementation = descriptor.ImplementationType != null
                ? FormatTypeName(descriptor.ImplementationType)
                : descriptor.ImplementationFactory != null
                    ? "<Factory>"
                    : descriptor.ImplementationInstance != null
                        ? "<Instance>"
                        : "<Unknown>";

            rows.Add(new[] { serviceName, lifetime, implementation });

            // 如果有多个实现（Keyed services），显示其他的
            foreach (var otherDescriptor in group.Skip(1))
            {
                var otherImpl = otherDescriptor.ImplementationType != null
                    ? FormatTypeName(otherDescriptor.ImplementationType)
                    : otherDescriptor.ImplementationFactory != null
                        ? "<Factory>"
                        : otherDescriptor.ImplementationInstance != null
                            ? "<Instance>"
                            : "<Unknown>";

                var key = otherDescriptor.ServiceKey != null ? $" [{otherDescriptor.ServiceKey}]" : "";
                rows.Add(new[] { "", otherDescriptor.Lifetime.ToString(), otherImpl + key });
            }
        }

        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                  Registered Services in DI Container                ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        PrintTable(new[] { "Service Type", "Lifetime", "Implementation" }, rows);
    }

    /// <summary>
    /// 打印模块树结构
    /// </summary>
    public static void PrintModuleTree(IEnumerable<ArtisanModule> modules)
    {
        var moduleList = modules.ToList();
        if (moduleList.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                      Loaded Modules (Empty)                        ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      Loaded Modules Tree                            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");

        var sb = new StringBuilder();
        for (int i = 0; i < moduleList.Count; i++)
        {
            var module = moduleList[i];
            var isLast = i == moduleList.Count - 1;
            var prefix = isLast ? "└── " : "├── ";
            var moduleName = module.GetType().Name;

            // 从 ModuleAttribute 获取 Level 信息
            var moduleAttr = module.GetType().GetCustomAttribute<ModuleAttribute>();
            var level = moduleAttr?.Level ?? ModuleLevel.Application;

            sb.AppendLine($"{prefix}{moduleName} (Level: {level})");
        }

        Console.WriteLine(sb.ToString());
    }

    /// <summary>
    /// 打印 API 端点列表
    /// </summary>
    public static void PrintApiEndpoints(WebApplication app)
    {
        try
        {
            var endpointDataSource = app.Services.GetService<EndpointDataSource>();
            if (endpointDataSource == null)
            {
                Console.WriteLine();
                Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                   API Endpoints (No EndpointDataSource)            ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
                return;
            }

            var endpoints = endpointDataSource.Endpoints
                .OfType<RouteEndpoint>()
                .OrderBy(e => e.RoutePattern?.RawText ?? "")
                .ToList();

            if (endpoints.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                   API Endpoints (Empty)                            ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
                return;
            }

            var rows = new List<string[]>();
            foreach (var endpoint in endpoints)
            {
                var route = endpoint.RoutePattern?.RawText ?? "<no route>";
                var methods = GetHttpMethods(endpoint);
                var name = endpoint.DisplayName ?? "<no name>";

                rows.Add(new[] { route, methods, name });
            }

            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                        API Endpoints List                         ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
            PrintTable(new[] { "Route", "HTTP Methods", "Endpoint Name" }, rows);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Error printing API endpoints: {ex.Message}");
        }
    }

    /// <summary>
    /// 打印 ASCII 表格
    /// </summary>
    private static void PrintTable(string[] headers, List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            PrintEmptyTable(headers);
            return;
        }

        // 计算每列的最大宽度
        var colWidths = new int[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            colWidths[i] = headers[i].Length;
        }

        foreach (var row in rows)
        {
            for (int i = 0; i < row.Length && i < headers.Length; i++)
            {
                colWidths[i] = Math.Max(colWidths[i], row[i].Length);
            }
        }

        // 打印表头
        PrintTableRow(headers, colWidths, "┌", "┬", "┐");
        PrintTableContent(headers, colWidths);
        PrintTableRow(headers, colWidths, "├", "┼", "┤");

        // 打印数据行
        for (int i = 0; i < rows.Count; i++)
        {
            PrintTableContent(rows[i], colWidths);
            if (i == rows.Count - 1)
            {
                PrintTableRow(headers, colWidths, "└", "┴", "┘");
            }
            else
            {
                PrintTableRow(headers, colWidths, "├", "┼", "┤");
            }
        }
    }

    /// <summary>
    /// 打印空表格
    /// </summary>
    private static void PrintEmptyTable(string[] headers)
    {
        var colWidths = headers.Select(h => h.Length).ToArray();

        PrintTableRow(headers, colWidths, "┌", "┬", "┐");
        PrintTableContent(headers, colWidths);
        PrintTableRow(headers, colWidths, "└", "┴", "┘");
    }

    /// <summary>
    /// 打印表格行分隔符
    /// </summary>
    private static void PrintTableRow(string[] headers, int[] colWidths, string left, string mid, string right)
    {
        var row = left;
        for (int i = 0; i < headers.Length; i++)
        {
            row += new string('─', colWidths[i] + 2);
            if (i < headers.Length - 1)
            {
                row += mid;
            }
        }
        row += right;
        Console.WriteLine(row);
    }

    /// <summary>
    /// 打印表格内容行
    /// </summary>
    private static void PrintTableContent(string[] cells, int[] colWidths)
    {
        var row = "│ ";
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i] ?? "";
            row += cell.PadRight(colWidths[i]) + " │ ";
        }
        Console.WriteLine(row);
    }

    /// <summary>
    /// 获取端点的 HTTP 方法
    /// </summary>
    private static string GetHttpMethods(RouteEndpoint endpoint)
    {
        var httpMethodMetadata = endpoint.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault();
        if (httpMethodMetadata == null)
        {
            return "ANY";
        }

        if (httpMethodMetadata.HttpMethods.Count == 0)
        {
            return "ANY";
        }

        return string.Join(", ", httpMethodMetadata.HttpMethods);
    }

    /// <summary>
    /// 格式化类型名称（简化版本）
    /// </summary>
    private static string FormatTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var genericArgs = type.GetGenericArguments();
            var argNames = string.Join(", ", genericArgs.Select(t => t.Name));
            return $"{genericDef.Name}<{argNames}>";
        }

        return type.Name;
    }
}
