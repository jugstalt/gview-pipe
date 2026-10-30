using gView.Pipe.Abstraction.Execution;

namespace gView.Pipe.Razor.Editor;

/// <summary>A log entry of a node, received while the execution runs.</summary>
public sealed record PipeLiveLine(string NodeId, string NodeName, PipeLogEntry Entry);
