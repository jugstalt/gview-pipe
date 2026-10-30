using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Core.Storage;

static internal class PipeStoreIds
{
    static public string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Ids are used as file names, so only a safe set of characters is allowed.</summary>
    static public string Validate(string id)
        => id.Length is > 0 and <= 100 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? id
            : throw new PipeException($"Invalid id '{id}': use letters, digits, '-' and '_' only");

    static public void Prepare(PipeWorkflow workflow)
    {
        if (string.IsNullOrEmpty(workflow.Id))
        {
            workflow.Id = NewId();
        }
        Validate(workflow.Id);

        var now = DateTimeOffset.UtcNow;
        workflow.CreatedAt ??= now;
        workflow.UpdatedAt = now;
    }

    static public void Prepare(PipeWorkflowGroup group, IEnumerable<PipeWorkflowGroup> existing)
    {
        if (string.IsNullOrEmpty(group.Id))
        {
            group.Id = NewId();
        }
        Validate(group.Id);

        group.Name = group.Name.Trim();
        if (group.Name.Length == 0)
        {
            throw new PipeException("A group needs a name");
        }
        if (existing.Any(g => g.Id != group.Id && g.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PipeException($"A group named '{group.Name}' already exists");
        }
        group.CreatedAt ??= DateTimeOffset.UtcNow;
    }

    static public void Prepare(PipeCredential credential)
    {
        if (string.IsNullOrEmpty(credential.Id))
        {
            credential.Id = NewId();
        }
        Validate(credential.Id);
    }
}
