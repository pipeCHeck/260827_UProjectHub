using System.Text.Json;

namespace UProjectHub.Core.Parsing;

public sealed class UProjectParser : IUProjectParser
{
    public async Task<UProjectParseResult> ParseAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(projectFilePath);
            var descriptor = await JsonSerializer.DeserializeAsync<UProjectDescriptor>(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return descriptor is null
                ? UProjectParseResult.Failure("The descriptor did not contain a JSON object.")
                : UProjectParseResult.Success(descriptor);
        }
        catch (JsonException exception)
        {
            return UProjectParseResult.Failure(exception.Message);
        }
        catch (IOException exception)
        {
            return UProjectParseResult.Failure(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return UProjectParseResult.Failure(exception.Message);
        }
    }
}
