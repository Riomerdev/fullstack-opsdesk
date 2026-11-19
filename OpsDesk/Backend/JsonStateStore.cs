using System.Text.Json;

public sealed class JsonStateStore<T> where T : class, new()
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public JsonStateStore(string path) => _path = path;

    public async Task<TResult> ReadAsync<TResult>(Func<T, TResult> read, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return read(await LoadAsync(ct)); }
        finally { _gate.Release(); }
    }

    public async Task<TResult> UpdateAsync<TResult>(Func<T, TResult> update, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            var result = update(state);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, Options, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temp, _path, true);
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task<T> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new T();
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct) ?? new T();
    }
}