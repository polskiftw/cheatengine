using System.Collections.Generic;

namespace GLoader
{
    internal sealed class ModSource
    {
        public ModSource(string id, string displayName, IReadOnlyList<string> sourceFiles)
        {
            Id = id;
            DisplayName = displayName;
            SourceFiles = sourceFiles;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> SourceFiles { get; }
    }
}
