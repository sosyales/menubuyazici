using System;

namespace MenuBuPrinterAgent.Printing;

internal sealed class InvalidPrinterException : Exception
{
    public InvalidPrinterException(string message) : base(message)
    {
    }

    public InvalidPrinterException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
