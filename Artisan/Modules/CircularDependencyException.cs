namespace Artisan.Modules;

/// <summary>
/// 循环依赖异常
/// </summary>
public class CircularDependencyException : Exception
{
    public Type[] DependencyChain { get; }

    public CircularDependencyException(Type[] chain)
        : base($"Circular dependency detected: {string.Join(" -> ", chain.Select(t => t.Name))}")
    {
        DependencyChain = chain;
    }
}
