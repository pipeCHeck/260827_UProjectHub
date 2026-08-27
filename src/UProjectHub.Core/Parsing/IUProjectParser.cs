namespace UProjectHub.Core.Parsing;

public interface IUProjectParser
{
    Task<UProjectParseResult> ParseAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default);
}
