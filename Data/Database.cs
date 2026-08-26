using Microsoft.Data.Sqlite;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Data;

public sealed record UpsertResult(int Succeeded, IReadOnlyList<(string ManifestPath, string Error)> Failed);

public sealed class Database
{
    private readonly string _connectionString;

    public Database()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OllamaModelExplorer");
        Directory.CreateDirectory(dir);
        _connectionString = $"Data Source={Path.Combine(dir, "models.db")}";
        Initialize();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void Initialize()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var dropLegacy = c.CreateCommand())
        {
            dropLegacy.Transaction = tx;
            dropLegacy.CommandText = "DROP TABLE IF EXISTS \"Model\";";
            dropLegacy.ExecuteNonQuery();
        }
        using (var create = c.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS Models (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL DEFAULT '', Publisher TEXT NOT NULL DEFAULT 'library',
                    Tag TEXT NOT NULL DEFAULT 'latest', SizeBytes INTEGER NOT NULL DEFAULT 0,
                    ModifiedUtc TEXT NOT NULL DEFAULT '', ManifestPath TEXT NOT NULL DEFAULT '',
                    Digest TEXT NOT NULL DEFAULT '', Installed INTEGER NOT NULL DEFAULT 1,
                    Description TEXT NOT NULL DEFAULT '', ParameterSize TEXT NOT NULL DEFAULT '',
                    Family TEXT NOT NULL DEFAULT '', Quantization TEXT NOT NULL DEFAULT '',
                    Format TEXT NOT NULL DEFAULT '', Context TEXT NOT NULL DEFAULT '',
                    CategoryText TEXT NOT NULL DEFAULT '', Capabilities TEXT NOT NULL DEFAULT '',
                    OllamaUrl TEXT NOT NULL DEFAULT '', MetadataUpdatedUtc TEXT NULL,
                    NewOnOllama INTEGER NOT NULL DEFAULT 0
                );
                """;
            create.ExecuteNonQuery();
        }
        MigrateColumns(c, tx);
        using (var dropIndex = c.CreateCommand())
        {
            dropIndex.Transaction = tx;
            dropIndex.CommandText = "DROP INDEX IF EXISTS UX_Models_ManifestPath;";
            dropIndex.ExecuteNonQuery();
        }
        using (var dedupe = c.CreateCommand())
        {
            dedupe.Transaction = tx;
            dedupe.CommandText = "DELETE FROM Models WHERE Id NOT IN (SELECT MAX(Id) FROM Models GROUP BY Publisher, Name, Tag);";
            dedupe.ExecuteNonQuery();
        }
        using (var indexes = c.CreateCommand())
        {
            indexes.Transaction = tx;
            indexes.CommandText = """
                CREATE INDEX IF NOT EXISTS IX_Models_Name ON Models(Name);
                CREATE INDEX IF NOT EXISTS IX_Models_Publisher ON Models(Publisher);
                CREATE INDEX IF NOT EXISTS IX_Models_Size ON Models(SizeBytes);
                CREATE INDEX IF NOT EXISTS IX_Models_Installed ON Models(Installed);
                CREATE UNIQUE INDEX IF NOT EXISTS UX_Models_Identity ON Models(Publisher, Name, Tag);
                """;
            indexes.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void MigrateColumns(SqliteConnection c, SqliteTransaction tx)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "PRAGMA table_info(Models);";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(1));
        }
        var columns = new (string Name, string Definition)[]
        {
            ("Name", "TEXT NOT NULL DEFAULT ''"), ("Publisher", "TEXT NOT NULL DEFAULT 'library'"),
            ("Tag", "TEXT NOT NULL DEFAULT 'latest'"), ("SizeBytes", "INTEGER NOT NULL DEFAULT 0"),
            ("ModifiedUtc", "TEXT NOT NULL DEFAULT ''"), ("ManifestPath", "TEXT NOT NULL DEFAULT ''"),
            ("Digest", "TEXT NOT NULL DEFAULT ''"), ("Installed", "INTEGER NOT NULL DEFAULT 1"),
            ("Description", "TEXT NOT NULL DEFAULT ''"), ("ParameterSize", "TEXT NOT NULL DEFAULT ''"),
            ("Family", "TEXT NOT NULL DEFAULT ''"), ("Quantization", "TEXT NOT NULL DEFAULT ''"),
            ("Format", "TEXT NOT NULL DEFAULT ''"), ("Context", "TEXT NOT NULL DEFAULT ''"),
            ("CategoryText", "TEXT NOT NULL DEFAULT ''"), ("Capabilities", "TEXT NOT NULL DEFAULT ''"),
            ("OllamaUrl", "TEXT NOT NULL DEFAULT ''"), ("MetadataUpdatedUtc", "TEXT NULL"),
            ("NewOnOllama", "INTEGER NOT NULL DEFAULT 0")
        };
        foreach (var (name, definition) in columns)
        {
            if (existing.Contains(name)) continue;
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"ALTER TABLE Models ADD COLUMN \"{name}\" {definition};";
            cmd.ExecuteNonQuery();
        }
        using var repair = c.CreateCommand();
        repair.Transaction = tx;
        repair.CommandText = """
            UPDATE Models SET
                Publisher = CASE WHEN Publisher IS NULL OR TRIM(Publisher)='' THEN 'library' ELSE Publisher END,
                Name = COALESCE(Name,''),
                Tag = CASE WHEN Tag IS NULL OR TRIM(Tag)='' THEN 'latest' ELSE Tag END,
                ParameterSize = CASE WHEN ParameterSize IS NULL OR LOWER(TRIM(ParameterSize)) IN ('unknown','n/a') THEN '' ELSE ParameterSize END,
                Quantization = CASE WHEN Quantization IS NULL OR LOWER(TRIM(Quantization)) IN ('unknown','n/a') THEN '' ELSE Quantization END;
            """;
        repair.ExecuteNonQuery();
    }

    public UpsertResult UpsertLocalModels(IEnumerable<ModelInfo> models)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        int succeeded = 0;
        var failed = new List<(string, string)>();
        foreach (var m in models)
        {
            try
            {
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO Models (Name, Publisher, Tag, SizeBytes, ModifiedUtc, ManifestPath, Digest, Installed,
                        Description, ParameterSize, Family, Quantization, Format, Context, CategoryText, Capabilities,
                        OllamaUrl, MetadataUpdatedUtc, NewOnOllama)
                    VALUES ($name,$publisher,$tag,$size,$modified,$path,$digest,1,$description,$parameters,$family,
                        $quantization,$format,$context,$categories,$capabilities,$url,$metadata,0)
                    ON CONFLICT(Publisher,Name,Tag) DO UPDATE SET
                        SizeBytes=excluded.SizeBytes, ModifiedUtc=excluded.ModifiedUtc, ManifestPath=excluded.ManifestPath,
                        Digest=excluded.Digest, Installed=1,
                        Description=CASE WHEN excluded.Description<>'' THEN excluded.Description ELSE Models.Description END,
                        ParameterSize=CASE WHEN excluded.ParameterSize<>'' AND LOWER(excluded.ParameterSize) NOT IN ('unknown','n/a') THEN excluded.ParameterSize ELSE Models.ParameterSize END,
                        Family=CASE WHEN excluded.Family<>'' THEN excluded.Family ELSE Models.Family END,
                        Quantization=CASE WHEN excluded.Quantization<>'' AND LOWER(excluded.Quantization) NOT IN ('unknown','n/a') THEN excluded.Quantization ELSE Models.Quantization END,
                        Format=CASE WHEN excluded.Format<>'' THEN excluded.Format ELSE Models.Format END,
                        Context=CASE WHEN excluded.Context<>'' THEN excluded.Context ELSE Models.Context END,
                        CategoryText=excluded.CategoryText,
                        Capabilities=CASE WHEN excluded.Capabilities<>'' THEN excluded.Capabilities ELSE Models.Capabilities END,
                        OllamaUrl=CASE WHEN excluded.OllamaUrl<>'' THEN excluded.OllamaUrl ELSE Models.OllamaUrl END,
                        MetadataUpdatedUtc=COALESCE(excluded.MetadataUpdatedUtc,Models.MetadataUpdatedUtc),
                        NewOnOllama=0;
                    """;
                Add(cmd,"$name",m.Name); Add(cmd,"$publisher",string.IsNullOrWhiteSpace(m.Publisher)?"library":m.Publisher);
                Add(cmd,"$tag",string.IsNullOrWhiteSpace(m.Tag)?"latest":m.Tag); Add(cmd,"$size",m.SizeBytes);
                Add(cmd,"$modified",m.ModifiedUtc.ToString("O")); Add(cmd,"$path",m.ManifestPath); Add(cmd,"$digest",m.Digest);
                Add(cmd,"$description",m.Description); Add(cmd,"$parameters",m.ParameterSize); Add(cmd,"$family",m.Family);
                Add(cmd,"$quantization",m.Quantization); Add(cmd,"$format",m.Format); Add(cmd,"$context",m.Context);
                Add(cmd,"$categories",m.CategoryText); Add(cmd,"$capabilities",m.Capabilities); Add(cmd,"$url",m.OllamaUrl);
                Add(cmd,"$metadata",m.MetadataUpdatedUtc?.ToString("O"));
                cmd.ExecuteNonQuery(); succeeded++;
            }
            catch(Exception ex) { failed.Add((m.DisplayName,ex.Message)); }
        }
        tx.Commit();
        return new UpsertResult(succeeded,failed);
    }

    private static void Add(SqliteCommand cmd,string name,object? value)=>cmd.Parameters.AddWithValue(name,value??DBNull.Value);

    public void MarkInstalledModels(IEnumerable<ModelInfo> currentModels)
    {
        using var c=Open(); using var tx=c.BeginTransaction();
        using(var reset=c.CreateCommand()){reset.Transaction=tx;reset.CommandText="UPDATE Models SET Installed=0;";reset.ExecuteNonQuery();}
        foreach(var m in currentModels){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE Models SET Installed=1 WHERE Publisher=$publisher AND Name=$name AND Tag=$tag;";Add(cmd,"$publisher",string.IsNullOrWhiteSpace(m.Publisher)?"library":m.Publisher);Add(cmd,"$name",m.Name);Add(cmd,"$tag",string.IsNullOrWhiteSpace(m.Tag)?"latest":m.Tag);cmd.ExecuteNonQuery();}
        tx.Commit();
    }

    public List<ModelInfo> GetAll()
    {
        var result=new List<ModelInfo>(); using var c=Open(); using var cmd=c.CreateCommand();
        cmd.CommandText="""SELECT Id,Name,Publisher,Tag,SizeBytes,ModifiedUtc,ManifestPath,Digest,Installed,Description,ParameterSize,Family,Quantization,Format,Context,CategoryText,Capabilities,OllamaUrl,MetadataUpdatedUtc,NewOnOllama FROM Models ORDER BY Name COLLATE NOCASE,Tag COLLATE NOCASE;""";
        using var r=cmd.ExecuteReader();
        while(r.Read()) result.Add(new ModelInfo{Id=r.GetInt64(0),Name=r.GetString(1),Publisher=r.GetString(2),Tag=r.GetString(3),SizeBytes=r.GetInt64(4),ModifiedUtc=ParseDate(r.GetString(5)),ManifestPath=r.GetString(6),Digest=r.GetString(7),Installed=r.GetInt64(8)!=0,Description=r.GetString(9),ParameterSize=r.GetString(10),Family=r.GetString(11),Quantization=r.GetString(12),Format=r.GetString(13),Context=r.GetString(14),CategoryText=r.GetString(15),Capabilities=r.GetString(16),OllamaUrl=r.GetString(17),MetadataUpdatedUtc=r.IsDBNull(18)?null:ParseDate(r.GetString(18)),NewOnOllama=r.GetInt64(19)!=0});
        return result;
    }

    private static DateTime ParseDate(string? value){if(DateTime.TryParse(value,null,System.Globalization.DateTimeStyles.RoundtripKind,out var dt))return dt.ToLocalTime();return DateTime.MinValue;}
}
