using System.IO;
using System.Text.Json;
using Shellf.Models;

namespace Shellf.Services;

public sealed class WorkspaceStorageService : IWorkspaceStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Shellf",
        "workspace_config.json");

    public WorkspaceConfig Load()
    {
        try
        {
            if (!File.Exists(_configPath))
                return new WorkspaceConfig();

            return JsonSerializer.Deserialize<WorkspaceConfig>(File.ReadAllText(_configPath))
                   ?? new WorkspaceConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or locked config must never block startup; start with a blank workspace.
            return new WorkspaceConfig();
        }
    }

    public void Save(WorkspaceConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}
