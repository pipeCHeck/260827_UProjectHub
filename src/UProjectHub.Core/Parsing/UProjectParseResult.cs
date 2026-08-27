namespace UProjectHub.Core.Parsing;

public sealed record UProjectParseResult
{
    private UProjectParseResult(
        bool isSuccess,
        UProjectDescriptor? descriptor,
        string? errorMessage)
    {
        IsSuccess = isSuccess;
        Descriptor = descriptor;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public UProjectDescriptor? Descriptor { get; }

    public string? ErrorMessage { get; }

    internal static UProjectParseResult Success(UProjectDescriptor descriptor) =>
        new(true, descriptor, null);

    internal static UProjectParseResult Failure(string errorMessage) =>
        new(false, null, errorMessage);
}
