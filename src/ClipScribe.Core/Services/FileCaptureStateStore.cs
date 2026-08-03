using System.Text.Json;
using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;

namespace ClipScribe.Core.Services;

public sealed class FileCaptureStateStore : ICaptureStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    public FileCaptureStateStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<CaptureRuntimeState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new CaptureRuntimeState(IsPaused: false);
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var state = await JsonSerializer.DeserializeAsync<CaptureRuntimeState>(stream, JsonOptions, cancellationToken);
            return state ?? new CaptureRuntimeState(IsPaused: false);
        }
        catch
        {
            return new CaptureRuntimeState(IsPaused: false);
        }
    }

    public async Task SaveAsync(CaptureRuntimeState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
    }
}
