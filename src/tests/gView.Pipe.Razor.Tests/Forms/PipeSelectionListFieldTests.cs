using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Razor.Forms;

namespace gView.Pipe.Razor.Tests.Forms;

public class PipeSelectionListFieldTests
{
    static private List<PipeParameterOption> Options(params string[] values) => values.Select(v => new PipeParameterOption(v)).ToList();

    [Fact]
    public void Stored_order_stays_new_options_are_appended_switched_off_and_gone_ones_are_missing()
    {
        var stored = new List<PipeSelection>
        {
            new() { Value = "b.ps1", Enabled = true },
            new() { Value = "gone.bat", Enabled = true },
            new() { Value = "a.py", Enabled = false }
        };

        var rows = PipeSelectionListField.Merge(stored, Options("a.py", "b.ps1", "new.ps1"));

        Assert.Equal(["b.ps1", "gone.bat", "a.py", "new.ps1"], rows.Select(r => r.Value));
        Assert.Equal([true, false, false, false], rows.Select(r => r.Enabled));
        Assert.True(rows[1].Missing);
        Assert.True(rows[3].IsNew);
    }

    [Fact]
    public void First_time_a_single_option_is_switched_on_several_are_all_off()
    {
        Assert.True(Assert.Single(PipeSelectionListField.Merge([], Options("only.bat"))).Enabled);
        Assert.All(PipeSelectionListField.Merge([], Options("a.bat", "b.bat")), r => Assert.False(r.Enabled));
    }
}
