using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WindowsQuotes.Settings;

namespace WindowsQuotes.Quotes;

/// <summary>Everything the widget keeps between runs apart from settings and quote lists.</summary>
public sealed class WidgetState
{
    public RotationState Rotation { get; set; } = new();
    public Dictionary<string, SourceStatus> Sources { get; set; } = [];
}

public sealed class SourceStatus
{
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Sent from the manager to the running widget: show this quote and hold it.</summary>
public sealed record PinRequest(Quote Quote, HoldDuration Duration);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(WidgetState))]
[JsonSerializable(typeof(PinRequest))]
[JsonSerializable(typeof(List<Quote>))]
public partial class DataJsonContext : JsonSerializerContext;

/// <summary>
/// JSON files in one folder. Writes go to a temp file and are then swapped in, so a crash
/// can't leave a half-written file, and a named mutex keeps the widget and manager
/// processes from overlapping.
/// </summary>
public sealed class DataStore
{
    private const string SettingsFile = "settings.json";
    private const string OwnQuotesFile = "my-quotes.json";
    private const string CacheFile = "cache.json";
    private const string StateFile = "state.json";
    private const string PinRequestFile = "pin-request.json";

    private readonly string _directory;
    private readonly string _mutexName;

    public DataStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToLowerInvariant()));
        _mutexName = $@"Local\WindowsQuotes.Data.{Convert.ToHexString(hash)[..16]}";
    }

    public static DataStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsQuotes"));

    public AppSettings LoadSettings() => Locked(() => Read(SettingsFile, DataJsonContext.Default.AppSettings) ?? new AppSettings());
    public void SaveSettings(AppSettings value) => Locked(() => Write(SettingsFile, value, DataJsonContext.Default.AppSettings));

    public List<Quote> LoadOwnQuotes() => Locked(() => Read(OwnQuotesFile, DataJsonContext.Default.ListQuote) ?? []);

    /// <summary>Reads, changes and saves the My Quotes list as one step.</summary>
    public List<Quote> UpdateOwnQuotes(Action<List<Quote>> change) => Locked(() =>
    {
        var quotes = Read(OwnQuotesFile, DataJsonContext.Default.ListQuote) ?? [];
        change(quotes);
        Write(OwnQuotesFile, quotes, DataJsonContext.Default.ListQuote);
        return quotes;
    });

    public List<Quote> LoadCache() => Locked(() => Read(CacheFile, DataJsonContext.Default.ListQuote) ?? []);
    public void SaveCache(List<Quote> value) => Locked(() => Write(CacheFile, value, DataJsonContext.Default.ListQuote));

    public WidgetState LoadState() => Locked(() => Read(StateFile, DataJsonContext.Default.WidgetState) ?? new WidgetState());
    public void SaveState(WidgetState value) => Locked(() => Write(StateFile, value, DataJsonContext.Default.WidgetState));

    public void RequestPin(PinRequest request) => Locked(() => Write(PinRequestFile, request, DataJsonContext.Default.PinRequest));

    /// <summary>Returns any waiting pin request and removes it.</summary>
    public PinRequest? TakePinRequest() => Locked(() =>
    {
        var request = Read(PinRequestFile, DataJsonContext.Default.PinRequest);
        if (request is not null)
        {
            File.Delete(Path.Combine(_directory, PinRequestFile));
        }
        return request;
    });

    private T? Read<T>(string file, JsonTypeInfo<T> type) where T : class
    {
        var path = Path.Combine(_directory, file);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), type);
        }
        catch (JsonException)
        {
            // Keep the damaged file for inspection and carry on with defaults.
            File.Move(path, path + ".corrupt", overwrite: true);
            return null;
        }
    }

    private void Write<T>(string file, T value, JsonTypeInfo<T> type)
    {
        var path = Path.Combine(_directory, file);
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, type));
        File.Move(temp, path, overwrite: true);
    }

    private void Locked(Action action) => Locked<object?>(() =>
    {
        action();
        return null;
    });

    private T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, _mutexName);
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The other process died while holding it. Each write is atomic, so the data is intact.
        }
        try
        {
            return action();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
