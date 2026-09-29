using System.Collections.Generic;

namespace Core.Corex
{
    /// <summary>
    /// In-memory representation of a COREX package (zipped or unpacked).
    /// </summary>
    public sealed class CorexPackage
    {
        public Manifest Manifest { get; set; } = new Manifest();
        public Dictionary<string, DrawingDocument> Drawings { get; set; } = new Dictionary<string, DrawingDocument>();
        public SheetsDocument? Sheets { get; set; }
        public ShortcutsDocument? Shortcuts { get; set; }
        public Dictionary<string, byte[]> GeometryBlobs { get; set; } = new Dictionary<string, byte[]>();
    }
}
