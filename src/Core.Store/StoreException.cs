namespace Core.Store;

public sealed class StoreException : Exception
{
    public StoreException(string message) : base(message) { }
    public StoreException(string message, Exception inner) : base(message, inner) { }
}
