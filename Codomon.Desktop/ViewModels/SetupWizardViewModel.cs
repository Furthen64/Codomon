using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class SetupWizardViewModel : INotifyPropertyChanged
{
    private const string WorkspaceFileName = "workspace.json";

    private int _currentStep = 1;
    private string _sourceProjectPath = string.Empty;
    private string _workspaceFolderPath = string.Empty;
    private string _workspaceName = string.Empty;
    private string _profileName = "Default";
    private string _newSystemName = string.Empty;
    private string _validationError = string.Empty;

    public const int TotalSteps = 4;

    public int CurrentStep
    {
        get => _currentStep;
        set { _currentStep = value; OnPropertyChanged(); OnPropertyChanged(nameof(StepTitle)); }
    }

    /// <summary>Step 1 — path to a .sln, .csproj, or project folder.</summary>
    public string SourceProjectPath
    {
        get => _sourceProjectPath;
        set { _sourceProjectPath = value; OnPropertyChanged(); ClearValidation(); }
    }

    /// <summary>Step 2 — target .codomon workspace folder.</summary>
    public string WorkspaceFolderPath
    {
        get => _workspaceFolderPath;
        set { _workspaceFolderPath = value; OnPropertyChanged(); ClearValidation(); }
    }

    /// <summary>Step 3 — human-readable workspace name.</summary>
    public string WorkspaceName
    {
        get => _workspaceName;
        set { _workspaceName = value; OnPropertyChanged(); ClearValidation(); }
    }

    /// <summary>Step 3 — default profile name.</summary>
    public string ProfileName
    {
        get => _profileName;
        set { _profileName = value; OnPropertyChanged(); ClearValidation(); }
    }

    /// <summary>Step 4 — text field for typing a new system name.</summary>
    public string NewSystemName
    {
        get => _newSystemName;
        set { _newSystemName = value; OnPropertyChanged(); }
    }

    /// <summary>Step 4 — list of system names to create.</summary>
    public ObservableCollection<string> SystemNames { get; } = new();

    /// <summary>Validation error message to display on the current step.</summary>
    public string ValidationError
    {
        get => _validationError;
        private set { _validationError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasValidationError)); }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    public string StepTitle => CurrentStep switch
    {
        1 => "Step 1 of 4 — Source Project",
        2 => "Step 2 of 4 — Workspace Folder",
        3 => "Step 3 of 4 — Names",
        4 => "Step 4 of 4 — Initial Systems (optional)",
        _ => string.Empty
    };

    /// <summary>
    /// Validates the current step. Returns <c>true</c> if valid so the wizard can advance.
    /// </summary>
    public bool ValidateCurrentStep()
    {
        ValidationError = string.Empty;

        switch (CurrentStep)
        {
            case 1:
                if (string.IsNullOrWhiteSpace(SourceProjectPath))
                {
                    ValidationError = "Please select a source project, solution, or folder.";
                    return false;
                }
                if (!System.IO.File.Exists(SourceProjectPath) && !System.IO.Directory.Exists(SourceProjectPath))
                {
                    ValidationError = "The selected path does not exist.";
                    return false;
                }
                break;

            case 2:
                if (string.IsNullOrWhiteSpace(WorkspaceFolderPath))
                {
                    ValidationError = "Please choose a workspace folder.";
                    return false;
                }
                // Folder may not exist yet — that is fine; we will create it.
                // But it must not point at an existing file.
                if (System.IO.File.Exists(WorkspaceFolderPath))
                {
                    ValidationError = "The chosen path is a file, not a folder.";
                    return false;
                }
                // Block non-empty folders — workspace initialisation requires an empty directory.
                if (System.IO.Directory.Exists(WorkspaceFolderPath))
                {
                    bool hasContents = System.IO.Directory.EnumerateFileSystemEntries(WorkspaceFolderPath).Any();
                    if (hasContents)
                    {
                        ValidationError = "The chosen folder is not empty. Please choose an empty folder or create a new one.";
                        return false;
                    }
                }
                break;

            case 3:
                if (string.IsNullOrWhiteSpace(WorkspaceName))
                {
                    ValidationError = "Please enter a workspace name.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(ProfileName))
                {
                    ValidationError = "Please enter a profile name.";
                    return false;
                }
                break;

            // Step 4 is entirely optional — always valid.
        }

        return true;
    }

    public void AddSystem()
    {
        var name = NewSystemName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (!SystemNames.Contains(name))
            SystemNames.Add(name);
        NewSystemName = string.Empty;
    }

    public void RemoveSystem(string name) => SystemNames.Remove(name);

    private void ClearValidation() => ValidationError = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
