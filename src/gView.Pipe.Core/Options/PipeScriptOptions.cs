namespace gView.Pipe.Core.Options;

/// <summary>Sandbox limits for JavaScript in expressions and Code nodes.</summary>
public class PipeScriptOptions
{
    public TimeSpan ExpressionTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Statement limit for a single expression; 0 = unlimited.</summary>
    public int ExpressionMaxStatements { get; set; } = 100_000;

    public TimeSpan CodeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public long MemoryLimitBytes { get; set; } = 64 * 1024 * 1024;

    public int MaxRecursionDepth { get; set; } = 256;
}
