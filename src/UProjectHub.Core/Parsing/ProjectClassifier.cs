using UProjectHub.Core.Models;

namespace UProjectHub.Core.Parsing;

public static class ProjectClassifier
{
    public static ProjectType Classify(UProjectDescriptor descriptor) =>
        descriptor.Modules is { Count: > 0 }
            ? ProjectType.Cpp
            : ProjectType.Blueprint;
}
