using System.IO;

namespace GUI_12_19.Models;

public class InspectionRecord
{
    public int Id { get; set; }
    public string SaveName { get; set; } = "";
    public string SaveAbsolutePath { get; set; } = "";
    public string Date { get; set; } = "";
    public int Type { get; set; } // 0: 簡易, 1: 精密

    public string ThumbnailPath => Path.Combine(SaveAbsolutePath, $"{SaveName}_omote.jpg");
    public string SimpleOmotePath => Path.Combine(SaveAbsolutePath, $"{SaveName}_omote.jpg");
    public string SimpleUraPath => Path.Combine(SaveAbsolutePath, $"{SaveName}_ura.jpg");
    public string PrecisionPcbOmotePath => Path.Combine(SaveAbsolutePath, $"{SaveName}_PCB_omote.jpg");
    public string PrecisionPcbUraPath => Path.Combine(SaveAbsolutePath, $"{SaveName}_PCB_ura.jpg");
    public string PrecisionCircuitOmotePath => Path.Combine(SaveAbsolutePath, $"{SaveName}_circuit_omote.jpg");
    public string PrecisionCircuitUraPath => Path.Combine(SaveAbsolutePath, $"{SaveName}_circuit_ura.jpg");
}
