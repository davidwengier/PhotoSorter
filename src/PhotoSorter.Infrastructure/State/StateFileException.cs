namespace PhotoSorter.Infrastructure.State;

public sealed class StateFileException : Exception
{
    public StateFileException(string message)
        : base(message)
    {
    }

    public StateFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
