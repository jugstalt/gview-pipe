namespace gView.Pipe.Abstraction;

public class PipeException : Exception
{
    public PipeException(string message) : base(message) { }
    public PipeException(string message, Exception innerException) : base(message, innerException) { }
}
