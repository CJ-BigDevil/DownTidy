using System.Text.Json.Serialization;

namespace DownTidy;

public sealed class RuleDefinition
{
    public string Name { get; set; } = "";
    public List<string> Extensions { get; set; } = new();
    public string TargetFolder { get; set; } = "";

    public RuleDefinition Clone() => new()
    {
        Name = Name,
        Extensions = new List<string>(Extensions),
        TargetFolder = TargetFolder
    };
}

public sealed class RuleDocument
{
    public List<RuleDefinition> Rules { get; set; } = new();
}

public sealed class OperationEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BatchId { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public string Status { get; set; } = "moved";
    public string Error { get; set; } = "";
}

public sealed class OperationEventArgs : EventArgs
{
    public required OperationEntry Entry { get; init; }
    public bool IsManualScan { get; init; }
}

public sealed class OrganizeSummary
{
    public int Moved { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public string BatchId { get; set; } = "";
}

public sealed class UndoSummary
{
    public int Restored { get; set; }
    public int Failed { get; set; }
    public string Message { get; set; } = "";
}

public sealed class PendingFile
{
    public DateTime LastEventUtc { get; set; } = DateTime.UtcNow;
    public long Length { get; set; } = -1;
    public DateTime LastWriteUtc { get; set; }
    public DateTime StableSinceUtc { get; set; }
    public int Attempts { get; set; }
}
