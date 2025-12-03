namespace Artisan.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ScanAssemblyAttribute : Attribute
{
    public string Pattern { get; }

    public ScanAssemblyAttribute(string pattern)
    {
        Pattern = pattern;
    }
}