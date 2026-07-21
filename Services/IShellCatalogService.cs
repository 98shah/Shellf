using Shellf.Models;

namespace Shellf.Services;

public interface IShellCatalogService
{
    /// <summary>Shells found on this machine (PowerShell / CMD / WSL).</summary>
    IReadOnlyList<ShellDefinition> GetInstalledShells();
}
