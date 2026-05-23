using System.Text.Json;

namespace CodexWidget.Core;

public interface IPreferencePathProvider
{
    string GetPreferenceFilePath();
}

public sealed class AppDataPreferencePathProvider : IPreferencePathProvider
{
    private const string SettingsFileName = "settings.json";
    private const string ApplicationDirectoryName = "CodexWidget";

    public string GetPreferenceFilePath()
    {
        var appData = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrWhiteSpace(appData))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appData = string.IsNullOrWhiteSpace(userProfile) ? Path.GetTempPath() : Path.Combine(userProfile, ".config");
        }

        return Path.Combine(appData, ApplicationDirectoryName, SettingsFileName);
    }
}

public sealed record PreferenceLoadResult
{
    public WidgetPreferences Preferences { get; init; } = WidgetPreferenceDefaults.Create();

    public bool UsedDefaults { get; init; }

    public StatusAvailability Availability { get; init; } = StatusAvailability.Available();

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}

public sealed record PreferenceSaveResult
{
    public StatusAvailability Availability { get; init; } = StatusAvailability.Available();

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}

public sealed class PreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IPreferencePathProvider _pathProvider;
    private readonly IPreferenceFileSystem _fileSystem;

    public PreferenceStore(IPreferencePathProvider pathProvider, IPreferenceFileSystem? fileSystem = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _fileSystem = fileSystem ?? new SystemPreferenceFileSystem();
    }

    public PreferenceLoadResult Load()
    {
        var defaults = WidgetPreferenceDefaults.Create();
        var path = _pathProvider.GetPreferenceFilePath();

        try
        {
            RecoverFromTemporaryFiles(path);

            if (!_fileSystem.FileExists(path))
            {
                return new PreferenceLoadResult
                {
                    Preferences = defaults,
                    UsedDefaults = true,
                    Availability = StatusAvailability.Available(),
                };
            }

            var json = _fileSystem.ReadAllText(path);
            var document = JsonSerializer.Deserialize<WidgetPreferencesDocument>(json, SerializerOptions);
            if (document is null)
            {
                return InvalidLoad(defaults, SourceDiagnosticCode.Malformed, "Preference file is empty or not a JSON object.");
            }

            var migration = WidgetPreferenceMigrator.MigrateToCurrent(document);
            if (!migration.Succeeded)
            {
                return InvalidLoad(defaults, migration.Diagnostic!);
            }

            var validation = WidgetPreferenceValidator.ValidateAndNormalize(migration.Document!);
            if (!validation.Succeeded)
            {
                return new PreferenceLoadResult
                {
                    Preferences = defaults,
                    UsedDefaults = true,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
                    Diagnostics = validation.Diagnostics,
                };
            }

            return new PreferenceLoadResult
            {
                Preferences = validation.Preferences!,
                UsedDefaults = false,
                Availability = StatusAvailability.Available(),
            };
        }
        catch (JsonException exception)
        {
            return InvalidLoad(
                defaults,
                SourceDiagnosticCode.Malformed,
                "Preference file contains malformed JSON.",
                exception.Message,
                CreateContext(path, "load", exception));
        }
        catch (IOException exception)
        {
            return InvalidLoad(
                defaults,
                SourceDiagnosticCode.Error,
                "Preference file could not be read.",
                exception.Message,
                CreateContext(path, "load", exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return InvalidLoad(
                defaults,
                SourceDiagnosticCode.Unavailable,
                "Preference file is not accessible.",
                exception.Message,
                CreateContext(path, "load", exception));
        }
    }

    public PreferenceSaveResult Save(WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var path = _pathProvider.GetPreferenceFilePath();
        var directoryPath = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return InvalidSave(
                SourceDiagnosticCode.Error,
                "Preference file path does not contain a parent directory.",
                context: CreateContext(path, "save"));
        }

        try
        {
            var document = WidgetPreferencesDocument.FromPreferences(preferences);
            var validation = WidgetPreferenceValidator.ValidateAndNormalize(document);
            if (!validation.Succeeded)
            {
                return new PreferenceSaveResult
                {
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
                    Diagnostics = validation.Diagnostics,
                };
            }

            _fileSystem.CreateDirectory(directoryPath);
            CleanupTemporaryFiles(path);

            var temporaryPath = BuildTemporaryPath(path);
            try
            {
                using (var stream = _fileSystem.CreateWriteStream(temporaryPath))
                {
                    JsonSerializer.Serialize(stream, WidgetPreferencesDocument.FromPreferences(validation.Preferences!), SerializerOptions);
                    if (stream is FileStream fileStream)
                    {
                        fileStream.Flush(flushToDisk: true);
                    }
                    else
                    {
                        stream.Flush();
                    }
                }

                _fileSystem.MoveFile(temporaryPath, path, overwrite: true);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }

            return new PreferenceSaveResult
            {
                Availability = StatusAvailability.Available(),
            };
        }
        catch (IOException exception)
        {
            return InvalidSave(
                SourceDiagnosticCode.Error,
                "Preference file could not be written.",
                exception.Message,
                CreateContext(path, "save", exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return InvalidSave(
                SourceDiagnosticCode.Unavailable,
                "Preference file location is not writable.",
                exception.Message,
                CreateContext(path, "save", exception));
        }
    }

    private static string BuildTemporaryPath(string path)
    {
        var fileName = Path.GetFileName(path);
        return Path.Combine(Path.GetDirectoryName(path)!, $"{fileName}.tmp.{Guid.NewGuid():N}");
    }

    private void RecoverFromTemporaryFiles(string path)
    {
        if (_fileSystem.FileExists(path))
        {
            CleanupTemporaryFiles(path);
            return;
        }

        var temporaryFiles = GetTemporaryFiles(path)
            .OrderBy(filePath => _fileSystem.GetLastWriteTimeUtc(filePath))
            .ToArray();

        if (temporaryFiles.Length == 0)
        {
            return;
        }

        _fileSystem.MoveFile(temporaryFiles[^1], path, overwrite: false);
        for (var index = 0; index < temporaryFiles.Length - 1; index++)
        {
            TryDelete(temporaryFiles[index]);
        }
    }

    private void CleanupTemporaryFiles(string path)
    {
        foreach (var temporaryPath in GetTemporaryFiles(path))
        {
            TryDelete(temporaryPath);
        }
    }

    private IEnumerable<string> GetTemporaryFiles(string path)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directoryPath) || !_fileSystem.DirectoryExists(directoryPath))
        {
            return Array.Empty<string>();
        }

        var fileName = Path.GetFileName(path);
        return _fileSystem.EnumerateFiles(directoryPath, $"{fileName}.tmp.*");
    }

    private void TryDelete(string path)
    {
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }

    private static PreferenceLoadResult InvalidLoad(
        WidgetPreferences defaults,
        SourceDiagnosticCode code,
        string summary,
        string? detail = null,
        IReadOnlyDictionary<string, string?>? context = null)
    {
        return InvalidLoad(defaults, CreateDiagnostic(code, summary, detail, context));
    }

    private static PreferenceLoadResult InvalidLoad(WidgetPreferences defaults, SourceDiagnostic diagnostic)
    {
        return new PreferenceLoadResult
        {
            Preferences = defaults,
            UsedDefaults = true,
            Availability = StatusAvailability.Unavailable(MapAvailabilityCode(diagnostic.Code), diagnostic.Summary),
            Diagnostics = new[] { diagnostic },
        };
    }

    private static PreferenceSaveResult InvalidSave(
        SourceDiagnosticCode code,
        string summary,
        string? detail = null,
        IReadOnlyDictionary<string, string?>? context = null)
    {
        var diagnostic = CreateDiagnostic(code, summary, detail, context);
        return new PreferenceSaveResult
        {
            Availability = StatusAvailability.Unavailable(MapAvailabilityCode(diagnostic.Code), diagnostic.Summary),
            Diagnostics = new[] { diagnostic },
        };
    }

    private static SourceDiagnostic CreateDiagnostic(
        SourceDiagnosticCode code,
        string summary,
        string? detail,
        IReadOnlyDictionary<string, string?>? context)
    {
        return SourceDiagnostic.Create(
            code,
            SourceDiagnosticSeverity.Error,
            summary,
            detail,
            context,
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyDictionary<string, string?> CreateContext(string preferenceFilePath, string operation, Exception? exception = null)
    {
        var context = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["preferenceFilePath"] = preferenceFilePath,
            ["operation"] = operation,
        };

        if (exception is not null)
        {
            context["exceptionType"] = exception.GetType().Name;
        }

        return context;
    }

    private static StatusAvailabilityCode MapAvailabilityCode(SourceDiagnosticCode code)
    {
        return code switch
        {
            SourceDiagnosticCode.MissingRequiredField => StatusAvailabilityCode.MissingRequiredField,
            SourceDiagnosticCode.Missing => StatusAvailabilityCode.Missing,
            SourceDiagnosticCode.Malformed => StatusAvailabilityCode.Malformed,
            SourceDiagnosticCode.Unavailable => StatusAvailabilityCode.Unavailable,
            SourceDiagnosticCode.Error => StatusAvailabilityCode.Error,
            _ => StatusAvailabilityCode.Error,
        };
    }
}

public interface IPreferenceFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    Stream CreateWriteStream(string path);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    void DeleteFile(string path);

    void CreateDirectory(string path);

    IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern);

    DateTime GetLastWriteTimeUtc(string path);
}

public sealed class SystemPreferenceFileSystem : IPreferenceFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public Stream CreateWriteStream(string path)
    {
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
    {
        File.Move(sourcePath, destinationPath, overwrite);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern)
    {
        return Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        return File.GetLastWriteTimeUtc(path);
    }
}
