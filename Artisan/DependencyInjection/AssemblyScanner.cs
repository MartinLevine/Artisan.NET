using System.Reflection;
using Artisan.Attributes;
using Artisan.Modules;
using Microsoft.AspNetCore.Mvc;

namespace Artisan.DependencyInjection
{
    public class ScanResult
    {
        // 所有的 Module 类型
        public List<Type> Modules { get; } = [];

        // 所有的 Service/Repository/Component 类型
        public List<Type> Injectables { get; } = [];

        // 所有的 Controller 类型 (或者包含 Controller 的程序集)
        public HashSet<Assembly> ControllerAssemblies { get; } = [];

        // 所有的 配置绑定 类型
        public List<Type> Configurations { get; } = [];
    }

    public class AssemblyScanner
    {
        public ScanResult Scan(Type entryType)
        {
            var result = new ScanResult();

            // 1. 初始化队列，包含入口程序集
            var pendingAssemblies = new Queue<Assembly>();
            var visitedAssemblies = new HashSet<string>();

            // 添加入口
            pendingAssemblies.Enqueue(entryType.Assembly);

            // 添加 [ScanAssembly] 指定的额外程序集
            var extraPatterns = entryType.GetCustomAttributes<ScanAssemblyAttribute>();
            foreach (var pattern in extraPatterns)
            {
                // 假设这里有个 Helper 能根据 glob 加载程序集
                var asms = AssemblyHelper.LoadFromPattern(pattern.Pattern);
                foreach (var asm in asms) pendingAssemblies.Enqueue(asm);
            }

            // 2. 开始广度优先搜索 (BFS)
            while (pendingAssemblies.Count > 0)
            {
                var assembly = pendingAssemblies.Dequeue();
                var asmName = assembly.GetName().Name!;

                // 去重
                if (!visitedAssemblies.Add(asmName)) continue;

                // 核心优化：跳过系统程序集，极大提升性能
                if (IsSystemAssembly(asmName)) continue;

                // ==========================================
                // 核心优化：在这个循环里完成所有类型的分类
                // ==========================================
                ProcessAssemblyTypes(assembly, result);

                // 3. 将引用的程序集加入队列 (递归)
                foreach (var refName in assembly.GetReferencedAssemblies())
                {
                    if (IsSystemAssembly(refName.Name) || visitedAssemblies.Contains(refName.Name!))
                        continue;

                    try
                    {
                        // 只有当引用的是相关业务集时才加载
                        // 这里可以加个前缀判断优化，比如只加载 "Artisan.*" 或 "MyApp.*"
                        var refAsm = Assembly.Load(refName);
                        pendingAssemblies.Enqueue(refAsm);
                    }
                    catch
                    {
                        // 忽略加载失败 (可能是环境缺失)
                    }
                }
            }

            return result;
        }

        private void ProcessAssemblyTypes(Assembly assembly, ScanResult result)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            bool hasController = false;

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;

                // A. 识别 Module
                if (typeof(ArtisanModule).IsAssignableFrom(type))
                {
                    result.Modules.Add(type);
                }
                // B. 识别 Service/Repository/Component
                else if (type.IsDefined(typeof(InjectableAttribute), true))
                {
                    result.Injectables.Add(type);
                }
                // C. 识别 Configuration
                else if (type.IsDefined(typeof(ConfigurationAttribute), true))
                {
                    result.Configurations.Add(type);
                }
                // D. 识别 Controller (原生 ControllerBase 或 [ApiController])
                // 这里我们不需要记录具体 Type，只需要标记 Assembly 给 MVC 用
                else if (!hasController && IsController(type))
                {
                    hasController = true;
                }
            }

            if (hasController)
            {
                result.ControllerAssemblies.Add(assembly);
            }
        }

        /// <summary>
        /// 需要过滤的系统程序集
        /// </summary>
        private bool IsSystemAssembly(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("System")
                   || name.StartsWith("Microsoft")
                   || name.StartsWith("mscorlib")
                   || name.StartsWith("Newtonsoft");
        }

        private bool IsController(Type type)
        {
            if (type.Name.EndsWith("Controller")) return true;
            if (type.IsDefined(typeof(ApiControllerAttribute), true)) return true;
            return false;
        }
    }
}