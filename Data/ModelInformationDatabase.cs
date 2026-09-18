using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Data;

/// <summary>
/// Separate, append-only database for model information. Connections are opened
/// only for short read/write/backup/restore operations and are immediately closed.
/// </summary>
public sealed class ModelInformationDatabase
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public string DatabasePath => _databasePath;

    public ModelInformationDatabase()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OllamaModelExplorer");
        Directory.CreateDirectory(dir);
        _databasePath = Path.Combine(dir, "model-information.db");
        _connectionString = $"Data Source={_databasePath};Cache=Private";
        Initialize();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ModelInformation (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Publisher TEXT NOT NULL DEFAULT 'library',
                Name TEXT NOT NULL,
                Tag TEXT NOT NULL DEFAULT 'latest',
                InformationText TEXT NOT NULL,
                AddedUtc TEXT NOT NULL,
                SourceUrl TEXT NOT NULL DEFAULT '',
                ContentHash TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Success'
            );
            CREATE INDEX IF NOT EXISTS IX_ModelInformation_Identity
                ON ModelInformation(Publisher, Name, Tag);
            CREATE INDEX IF NOT EXISTS IX_ModelInformation_Hash
                ON ModelInformation(ContentHash);
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public List<ModelInformation> LoadAll()
    {
        var result = new List<ModelInformation>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Publisher, Name, Tag, InformationText, AddedUtc,
                   SourceUrl, ContentHash, Status
            FROM ModelInformation
            ORDER BY Publisher COLLATE NOCASE, Name COLLATE NOCASE, Tag COLLATE NOCASE, AddedUtc;
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new ModelInformation
            {
                Id = r.GetInt64(0),
                Publisher = r.GetString(1),
                Name = r.GetString(2),
                Tag = r.GetString(3),
                InformationText = r.GetString(4),
                AddedUtc = ParseUtc(r.GetString(5)),
                SourceUrl = r.GetString(6),
                ContentHash = r.GetString(7),
                Status = r.GetString(8)
            });
        }
        return result;
    }

    public int SeedFromModels(IEnumerable<ModelInfo> models)
    {
        var candidates = models
            .Where(m => !string.IsNullOrWhiteSpace(m.Description) ||
                        !string.IsNullOrWhiteSpace(m.Capabilities) ||
                        !string.IsNullOrWhiteSpace(m.OllamaUrl))
            .ToList();
        if (candidates.Count == 0) return 0;

        using var c = Open();
        using var tx = c.BeginTransaction();
        int added = 0;
        foreach (var m in candidates)
        {
            var text = BuildInformationText(m);
            if (string.IsNullOrWhiteSpace(text)) continue;
            using var exists = c.CreateCommand();
            exists.Transaction = tx;
            exists.CommandText = """
                SELECT 1 FROM ModelInformation
                WHERE Publisher=$publisher AND Name=$name AND Tag=$tag
                LIMIT 1;
                """;
            Add(exists, "$publisher", NormalizePublisher(m.Publisher));
            Add(exists, "$name", m.Name);
            Add(exists, "$tag", NormalizeTag(m.Tag));
            if (exists.ExecuteScalar() is not null) continue;

            Insert(c, tx, m.Publisher, m.Name, m.Tag, text, DateTime.UtcNow,
                m.OllamaUrl, ComputeHash(text), "Original information");
            added++;
        }
        tx.Commit();
        return added;
    }

    public int AppendUpdates(IEnumerable<ModelInformation> updates)
    {
        var candidates = updates.Where(x => !string.IsNullOrWhiteSpace(x.InformationText)).ToList();
        if (candidates.Count == 0) return 0;
        using var c = Open();
        using var tx = c.BeginTransaction();
        int added = 0;
        foreach (var update in candidates)
        {
            var publisher = NormalizePublisher(update.Publisher);
            var tag = NormalizeTag(update.Tag);
            var hash = string.IsNullOrWhiteSpace(update.ContentHash)
                ? ComputeHash(update.InformationText) : update.ContentHash;
            using var exists = c.CreateCommand();
            exists.Transaction = tx;
            exists.CommandText = """
                SELECT 1 FROM ModelInformation
                WHERE Publisher=$publisher AND Name=$name AND Tag=$tag AND ContentHash=$hash
                LIMIT 1;
                """;
            Add(exists, "$publisher", publisher);
            Add(exists, "$name", update.Name);
            Add(exists, "$tag", tag);
            Add(exists, "$hash", hash);
            if (exists.ExecuteScalar() is not null) continue;
            Insert(c, tx, publisher, update.Name, tag, update.InformationText,
                update.AddedUtc == default ? DateTime.UtcNow : update.AddedUtc,
                update.SourceUrl, hash,
                string.IsNullOrWhiteSpace(update.Status) ? "Update" : update.Status);
            added++;
        }
        tx.Commit();
        return added;
    }

    public void BackupTo(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("A backup destination is required.", nameof(destinationPath));
        var fullDestination = Path.GetFullPath(destinationPath);
        if (string.Equals(fullDestination, Path.GetFullPath(_databasePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The backup destination cannot be the active database file.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        using var source = Open();
        using var destination = new SqliteConnection($"Data Source={fullDestination};Cache=Private");
        destination.Open();
        source.BackupDatabase(destination);
    }

    public void RestoreFrom(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The selected backup database was not found.", sourcePath);
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(_databasePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected backup is already the active model-information database.");

        using (var validation = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Cache=Private"))
        {
            validation.Open();
            using var command = validation.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ModelInformation';";
            if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                throw new InvalidDataException("The selected file is not a valid OllamaModelExplorer model-information database.");
        }

        var safetyBackup = _databasePath + ".pre-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db";
        if (File.Exists(_databasePath)) BackupTo(safetyBackup);

        var temp = _databasePath + ".restore-" + Guid.NewGuid().ToString("N");
        File.Copy(sourcePath, temp, true);
        try { File.Copy(temp, _databasePath, true); }
        finally { TryDelete(temp); }
    }

    private static void Insert(SqliteConnection c, SqliteTransaction tx, string publisher, string name,
        string tag, string text, DateTime addedUtc, string sourceUrl, string hash, string status)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO ModelInformation
                (Publisher, Name, Tag, InformationText, AddedUtc, SourceUrl, ContentHash, Status)
            VALUES
                ($publisher, $name, $tag, $text, $added, $url, $hash, $status);
            """;
        Add(cmd, "$publisher", NormalizePublisher(publisher));
        Add(cmd, "$name", name);
        Add(cmd, "$tag", NormalizeTag(tag));
        Add(cmd, "$text", text);
        Add(cmd, "$added", addedUtc.ToString("O"));
        Add(cmd, "$url", sourceUrl ?? "");
        Add(cmd, "$hash", hash);
        Add(cmd, "$status", status);
        cmd.ExecuteNonQuery();
    }

    private static string BuildInformationText(ModelInfo m)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(m.Description)) parts.Add("Description:\r\n" + m.Description.Trim());
        if (!string.IsNullOrWhiteSpace(m.Capabilities)) parts.Add("Capabilities:\r\n" + m.Capabilities.Replace("|", ", ").Trim());
        if (!string.IsNullOrWhiteSpace(m.OllamaUrl)) parts.Add("Source URL:\r\n" + m.OllamaUrl.Trim());
        return string.Join("\r\n\r\n", parts);
    }

    public static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizePublisher(string? value) => string.IsNullOrWhiteSpace(value) ? "library" : value.Trim();
    private static string NormalizeTag(string? value) => string.IsNullOrWhiteSpace(value) ? "latest" : value.Trim();
    private static DateTime ParseUtc(string value) => DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt.ToUniversalTime() : DateTime.MinValue;
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
