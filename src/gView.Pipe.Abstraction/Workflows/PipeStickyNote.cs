namespace gView.Pipe.Abstraction.Workflows;

/// <summary>A note on the canvas of the editor (ADR 0024). The engine ignores it.</summary>
public sealed class PipeStickyNote
{
    public const double DefaultWidth = 220;
    public const double DefaultHeight = 140;

    /// <summary>Colors the editor offers (css classes <c>gview-pipe-note-*</c>).</summary>
    static public readonly string[] Colors = ["yellow", "green", "blue", "red", "purple", "gray"];

    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    public string Color { get; set; } = "yellow";

    public PipePosition Position { get; set; } = new();

    public double Width { get; set; } = DefaultWidth;

    public double Height { get; set; } = DefaultHeight;
}
