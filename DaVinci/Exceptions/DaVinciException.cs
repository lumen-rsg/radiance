namespace DaVinci.Exceptions;

public class DaVinciException : Exception
{
    public DaVinciException() { }
    public DaVinciException(string message) : base(message) { }
    public DaVinciException(string message, Exception inner) : base(message, inner) { }
}
