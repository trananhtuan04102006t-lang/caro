using CaroNet.Storage.Database;

namespace CaroNet.Storage.Tests;

internal sealed class TemporarySqliteDatabase : IDisposable
{
    private TemporarySqliteDatabase(string path)
    {
        Path = path;
        new DatabaseInitializer(path).Initialize();
    }

    public string Path { get; }

    public static TemporarySqliteDatabase Create()
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CaroNet.Storage.Tests");
        string databasePath = System.IO.Path.Combine(
            directory,
            $"{Guid.NewGuid():N}.db");

        return new TemporarySqliteDatabase(databasePath);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // Windows có thể giữ file thêm một nhịp sau khi SQLite đóng connection.
        }
    }
}
