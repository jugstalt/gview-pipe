using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Razor.Editor;

/// <summary>Canvas model of a sticky note (ADR 0024). It has no ports.</summary>
public sealed class PipeNoteModel : NodeModel
{
    public PipeNoteModel(PipeStickyNote note)
        : base(note.Id, new Point(note.Position.X, note.Position.Y))
    {
        Note = note;
        Size = new Size(note.Width, note.Height);
        // behind nodes and links
        Order = -1;
    }

    public PipeStickyNote Note { get; }

    /// <summary>Applies width and height of <see cref="Note"/> to the canvas model.</summary>
    public void ApplySize()
    {
        Size = new Size(Note.Width, Note.Height);
        Refresh();
    }
}
