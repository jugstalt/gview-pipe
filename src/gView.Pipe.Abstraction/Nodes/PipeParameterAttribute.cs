namespace gView.Pipe.Abstraction.Nodes;

/// <summary>UI hints for a property of a node's parameter class.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PipeParameterAttribute : Attribute
{
    public string? Label { get; set; }
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public int Order { get; set; }
    public bool Multiline { get; set; }

    /// <summary>Whether the editor offers <c>{{ }}</c> expressions for this field.</summary>
    public bool Expressions { get; set; } = true;

    /// <summary>
    /// Source code (e.g. JavaScript): edited in a large monospace field without expressions.
    /// Read such values from <see cref="IPipeNodeContext.RawParameters"/>, because
    /// <see cref="IPipeNodeContext.GetParameters{TParameters}"/> evaluates <c>{{ }}</c> in every string.
    /// </summary>
    public bool Code { get; set; }

    /// <summary>Language of a <see cref="Code"/> field for syntax highlighting, e.g. <c>javascript</c> or <c>sql</c>.</summary>
    public string? CodeLanguage { get; set; }

    /// <summary>
    /// Name of another property of the parameter class (use <c>nameof</c>): this field shows only while that property
    /// has one of <see cref="ShowWhenValues"/>. A hidden field keeps its value. See ADR 0043.
    /// </summary>
    public string? ShowWhen { get; set; }

    /// <summary>
    /// Values of <see cref="ShowWhen"/> that show this field, compared without case: enum names, option values,
    /// <c>true</c>/<c>false</c> for a bool.
    /// </summary>
    public string[]? ShowWhenValues { get; set; }

    /// <summary>Rarely needed: the field is in the folded "Advanced" section of the form.</summary>
    public bool Advanced { get; set; }
}

/// <summary>Label and description of an enum member offered as a parameter value (dropdown and node reference).</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class PipeEnumValueAttribute : Attribute
{
    public PipeEnumValueAttribute(string description)
    {
        Description = description;
    }

    /// <summary>One line: what this choice does.</summary>
    public string Description { get; }

    /// <summary>Shown instead of the humanized member name.</summary>
    public string? Label { get; set; }
}

/// <summary>
/// Marks a string property that holds the id of a stored credential.
/// Restricts the selectable credentials to the given credential type ids.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PipeCredentialAttribute : Attribute
{
    public PipeCredentialAttribute(params string[] credentialTypeIds)
    {
        CredentialTypeIds = credentialTypeIds.Select(Guid.Parse).ToArray();
    }

    public Guid[] CredentialTypeIds { get; }
}

/// <summary>Marks a string property that holds the id of a stored workflow; the editor offers a workflow picker.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PipeWorkflowReferenceAttribute : Attribute
{
}

/// <summary>
/// Marks the property of a Form Trigger that holds the fields of the form (a JSON array of
/// <see cref="Portal.PipeFormField"/>): the editor offers a field editor instead of raw JSON.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PipeFormFieldsAttribute : Attribute
{
}
