using System.Collections.Generic;

namespace Core.Corex
{
    public sealed class SheetsDocument
    {
        public string Schema { get; set; } = "corex.sheets/1";
        public SheetSetInfo? SheetSet { get; set; }
        public List<SheetEntry> Sheets { get; set; } = new List<SheetEntry>();
    }

    public sealed class SheetSetInfo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }

    public sealed class SheetEntry
    {
        public string Number { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Revision { get; set; }
        public string Drawing { get; set; } = "";
        public string Layout { get; set; } = "";
        public string Source { get; set; } = "";
    }
}
