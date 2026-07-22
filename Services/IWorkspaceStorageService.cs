using Shellf.Models;

namespace Shellf.Services;

public interface IWorkspaceStorageService
{
    /// <summary>Loads the saved workspace, or an empty one if none exists or the file is unreadable.</summary>
    WorkspaceConfig Load();

    void Save(WorkspaceConfig config);

    /// <summary>Free-form notes shown in the notes panel; empty when none exist.</summary>
    string LoadNotes();

    void SaveNotes(string text);
}
