using System.Text.Json.Serialization;

namespace Packman.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReturnCodeType { Success, SoftReboot, HardReboot, Retry, Failed }

/// <summary>
/// A single Intune Win32 app return code mapping (exit code → outcome).
/// </summary>
public class ReturnCodeInfo
{
    public int Code { get; set; }
    public ReturnCodeType Type { get; set; } = ReturnCodeType.Success;

    /// <summary>The value Graph expects for win32LobAppReturnCode.type.</summary>
    [JsonIgnore]
    public string GraphType => Type switch
    {
        ReturnCodeType.SoftReboot => "softReboot",
        ReturnCodeType.HardReboot => "hardReboot",
        ReturnCodeType.Retry => "retry",
        ReturnCodeType.Failed => "failed",
        _ => "success"
    };

    /// <summary>The codes Intune applies when nothing else is configured.</summary>
    public static List<ReturnCodeInfo> Defaults() => new()
    {
        new() { Code = 0,    Type = ReturnCodeType.Success },
        new() { Code = 3010, Type = ReturnCodeType.SoftReboot },
        new() { Code = 1641, Type = ReturnCodeType.HardReboot },
        new() { Code = 1618, Type = ReturnCodeType.Retry }
    };
}
