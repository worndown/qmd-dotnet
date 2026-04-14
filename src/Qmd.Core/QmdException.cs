namespace Qmd.Core;

/// <summary>
/// Base exception for QMD domain errors.
/// </summary>
public class QmdException : Exception
{
    public QmdException(string message) : base(message) { }
    public QmdException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an LLM operation fails or is not configured.
/// </summary>
public class QmdModelException : QmdException
{
    public QmdModelException(string message) : base(message) { }
    public QmdModelException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown for malformed search queries.
/// </summary>
public class QmdQueryException : QmdException
{
    public QmdQueryException(string message) : base(message) { }
}
