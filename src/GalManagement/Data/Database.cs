using System.IO;
using Microsoft.Data.Sqlite;

namespace GalManagement.Data;

/// <summary>负责数据库文件位置、目录初始化与建表。</summary>
public class Database
{
    public string DataDirectory { get; }
    public string CoversDirectory { get; }
    public string BackgroundsDirectory { get; }
    public string IconsDirectory { get; }

    private readonly string _connectionString;

    public Database()
    {
        DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        CoversDirectory = Path.Combine(DataDirectory, "covers");
        BackgroundsDirectory = Path.Combine(DataDirectory, "backgrounds");
        IconsDirectory = Path.Combine(DataDirectory, "icons");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CoversDirectory);
        Directory.CreateDirectory(BackgroundsDirectory);
        Directory.CreateDirectory(IconsDirectory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(DataDirectory, "gamelibrary.db"),
        }.ToString();

        Initialize();
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Games (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                Name          TEXT    NOT NULL,
                CoverPath     TEXT,
                Rating        REAL,
                Summary       TEXT,
                Status        TEXT    NOT NULL DEFAULT 'Completed',
                Developer     TEXT,
                CompletedDate TEXT,
                PlayTimeHours REAL,
                CreatedAt     TEXT    NOT NULL,
                UpdatedAt     TEXT    NOT NULL,
                ThumbOffsetX  REAL    NOT NULL DEFAULT 0.5,
                ThumbOffsetY  REAL    NOT NULL DEFAULT 0.5,
                LaunchPath    TEXT
            );
            """;
        cmd.ExecuteNonQuery();

        // 迁移:旧数据库的 Platform 列改名为 Developer(保留原数据)
        if (ColumnExists(conn, "Games", "Platform") && !ColumnExists(conn, "Games", "Developer"))
        {
            using var rename = conn.CreateCommand();
            rename.CommandText = "ALTER TABLE Games RENAME COLUMN Platform TO Developer";
            rename.ExecuteNonQuery();
        }

        // 迁移:为旧数据库补充缩略图偏移列
        foreach (var (col, def) in new[] { ("ThumbOffsetX", "0.5"), ("ThumbOffsetY", "0.5") })
        {
            if (ColumnExists(conn, "Games", col))
                continue;
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE Games ADD COLUMN {col} REAL NOT NULL DEFAULT {def}";
            alter.ExecuteNonQuery();
        }

        if (!ColumnExists(conn, "Games", "LaunchPath"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Games ADD COLUMN LaunchPath TEXT";
            alter.ExecuteNonQuery();
        }

        using var reviewCmd = conn.CreateCommand();
        reviewCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS GameReviews (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                GameId       INTEGER NOT NULL,
                Title        TEXT    NOT NULL,
                Rating       REAL    NOT NULL DEFAULT 0,
                Comment      TEXT,
                CoverPath    TEXT,
                ThumbOffsetX REAL    NOT NULL DEFAULT 0.5,
                ThumbOffsetY REAL    NOT NULL DEFAULT 0.5,
                CreatedAt    TEXT    NOT NULL,
                UpdatedAt    TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_GameReviews_GameId ON GameReviews (GameId);
            """;
        reviewCmd.ExecuteNonQuery();

        // 迁移:为旧数据库补充评价封面列
        if (!ColumnExists(conn, "GameReviews", "CoverPath"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE GameReviews ADD COLUMN CoverPath TEXT";
            alter.ExecuteNonQuery();
        }

        foreach (var (col, def) in new[] { ("ThumbOffsetX", "0.5"), ("ThumbOffsetY", "0.5") })
        {
            if (ColumnExists(conn, "GameReviews", col))
                continue;
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE GameReviews ADD COLUMN {col} REAL NOT NULL DEFAULT {def}";
            alter.ExecuteNonQuery();
        }
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == column)
                return true;
        }
        return false;
    }
}
