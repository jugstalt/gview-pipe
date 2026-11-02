using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Core.Tests.Storage;

public sealed class PipeStickyNoteTests
{
    [Fact]
    public void Notes_survive_a_round_trip()
    {
        var workflow = new PipeWorkflow { Id = "w", Name = "W" };
        workflow.StickyNotes = [new PipeStickyNote { Id = "n1", Text = "Set up the credential first", Color = "blue", Width = 300 }];

        var copy = PipeJson.Clone(workflow);

        var note = Assert.Single(copy.StickyNotes!);
        Assert.Equal("Set up the credential first", note.Text);
        Assert.Equal("blue", note.Color);
        Assert.Equal(300, note.Width);
    }

    [Fact]
    public void A_workflow_without_notes_loads_and_writes_no_list()
    {
        var workflow = PipeJson.Deserialize<PipeWorkflow>("""{ "id": "w", "name": "W" }""")!;

        Assert.Null(workflow.StickyNotes);
        Assert.Equal("no changes", PipeWorkflowChanges.Describe(workflow, PipeJson.Clone(workflow)));
    }

    [Fact]
    public void Changed_notes_show_up_as_one_entry()
    {
        var before = new PipeWorkflow { Id = "w", Name = "W" };
        var after = PipeJson.Clone(before);
        after.StickyNotes = [new PipeStickyNote { Id = "n1", Text = "a" }, new PipeStickyNote { Id = "n2", Text = "b" }];

        Assert.Equal("notes", PipeWorkflowChanges.Describe(before, after));
    }
}
