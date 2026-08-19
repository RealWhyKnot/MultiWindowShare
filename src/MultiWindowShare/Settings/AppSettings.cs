using System.Text.Json;

namespace MultiWindowShare.Settings;

public sealed class AppSettings
{
    public string? SinkDeviceId { get; set; }

    public int CanvasWidth { get; set; } = 1920;

    public int CanvasHeight { get; set; } = 1080;

    // Main window geometry; Width 0 means never saved.
    public int MainWindowX { get; set; }

    public int MainWindowY { get; set; }

    public int MainWindowWidth { get; set; }

    public int MainWindowHeight { get; set; }

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MultiWindowShare",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // A settings file that cannot be written is not worth failing a capture session over.
        }
    }
}
