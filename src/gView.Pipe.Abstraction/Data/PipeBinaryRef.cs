namespace gView.Pipe.Abstraction.Data;

/// <summary>Reference to binary content held by an <see cref="IPipeBinaryStore"/>.</summary>
public sealed record PipeBinaryRef(string Id, string FileName, string MimeType, long Length);
