using Artisan.Modules;

namespace Artisan.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class ModuleAttribute : Attribute
{
    public ModuleLevel Level { get; set; } = ModuleLevel.Application;
    
    public int Order { get; set; } = 0;

    public Type[] DependsOn { get; set; } = [];
}