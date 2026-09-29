using System.Collections.Generic;

namespace Core.Corex
{
    public sealed class ShortcutsDocument
    {
        public string Schema { get; set; } = "corex.shortcuts/1";
        public ShortcutFolder Folder { get; set; } = new ShortcutFolder();
        public List<ShortcutEntry> Shortcuts { get; set; } = new List<ShortcutEntry>();
    }

    public sealed class ShortcutFolder
    {
        public string Path { get; set; } = "";
        public bool Exists { get; set; }
    }

    public sealed class ShortcutEntry
    {
        public string Name { get; set; } = "";
        public string ObjectType { get; set; } = "";
        public string? ShortcutFile { get; set; }
        public string SourcePath { get; set; } = "";
        public string? SourceDrawing { get; set; }
        public string? SourceHandle { get; set; }
        public string Status { get; set; } = "";
    }
}
