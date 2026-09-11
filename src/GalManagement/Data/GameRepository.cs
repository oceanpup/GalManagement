using System.Text;
using GalManagement.Models;
using Microsoft.Data.Sqlite;

namespace GalManagement.Data;

/// <summary>游戏记录的数据访问(手写参数化 SQL)。</summary>
public class GameRepository
{
    private const string Columns =
        "Id, Name, CoverPath, Rating, Summary, Status, Developer, CompletedDate, PlayTimeHours, CreatedAt, UpdatedAt, ThumbOffsetX, ThumbOffsetY, LaunchPath, " +
        "(SELECT AVG(GameReviews.Rating) FROM GameReviews WHERE GameReviews.GameId = Games.Id) AS AvgReviewRating";

    private readonly Database _db;

    public GameRepository(Database db) => _db = db;

    public List<Game> Search(GameFilter filter)
    {
        var sql = new StringBuilder($"SELECT {Columns} FROM Games");
        var where = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
            where.Add("(Name LIKE @kw OR Developer LIKE @kw OR EXISTS " +
                      "(SELECT 1 FROM GameTags gt JOIN Tags t ON t.Id = gt.TagId " +
                      "WHERE gt.GameId = Games.Id AND t.Name LIKE @kw))");
        if (filter.Status.HasValue)
            where.Add("Status = @status");
        if (!string.IsNullOrWhiteSpace(filter.Developer))
            where.Add("Developer = @developer");

        var tagList = filter.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
        for (var i = 0; i < tagList.Count; i++)
            where.Add($"EXISTS (SELECT 1 FROM GameTags gt JOIN Tags t ON t.Id = gt.TagId " +
                      $"WHERE gt.GameId = Games.Id AND t.Name = @tag{i})");

        if (where.Count > 0)
            sql.Append(" WHERE ").Append(string.Join(" AND ", where));

        var sortCol = filter.SortBy switch
        {
            "Rating" => "Rating",
            "PlayTimeHours" => "PlayTimeHours",
            "CompletedDate" => "CompletedDate",
            "UpdatedAt" => "UpdatedAt",
            _ => "Name",
        };
        sql.Append(" ORDER BY ").Append(sortCol).Append(filter.Ascending ? " ASC" : " DESC");
        if (sortCol != "Name")
            sql.Append(", Name ASC");

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
            cmd.Parameters.AddWithValue("@kw", $"%{filter.Keyword}%");
        if (filter.Status.HasValue)
            cmd.Parameters.AddWithValue("@status", filter.Status.Value.ToString());
        if (!string.IsNullOrWhiteSpace(filter.Developer))
            cmd.Parameters.AddWithValue("@developer", filter.Developer);
        for (var i = 0; i < tagList.Count; i++)
            cmd.Parameters.AddWithValue($"@tag{i}", tagList[i]);

        var list = new List<Game>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadGame(reader));
        return list;
    }

    public List<Game> GetAll() => Search(new GameFilter());

    public Game? GetById(int id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM Games WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadGame(reader) : null;
    }

    public int Add(Game game)
    {
        game.CreatedAt = DateTime.Now;
        game.UpdatedAt = game.CreatedAt;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Games (Name, CoverPath, Rating, Summary, Status, Developer, CompletedDate, PlayTimeHours, CreatedAt, UpdatedAt, ThumbOffsetX, ThumbOffsetY, LaunchPath)
            VALUES (@name, @cover, @rating, @summary, @status, @developer, @completed, @hours, @created, @updated, @thumbX, @thumbY, @launch);
            SELECT last_insert_rowid();
            """;
        AddParams(cmd, game);

        game.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return game.Id;
    }

    public void Update(Game game)
    {
        game.UpdatedAt = DateTime.Now;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Games SET
                Name = @name, CoverPath = @cover, Rating = @rating, Summary = @summary,
                Status = @status, Developer = @developer, CompletedDate = @completed,
                PlayTimeHours = @hours, UpdatedAt = @updated,
                ThumbOffsetX = @thumbX, ThumbOffsetY = @thumbY, LaunchPath = @launch
            WHERE Id = @id;
            """;
        AddParams(cmd, game);
        cmd.Parameters.AddWithValue("@id", game.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>只更新游玩时长与更新时间(时长监测落库用,避免整行覆盖)。</summary>
    public void UpdatePlayTimeHours(int id, double hours)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Games SET PlayTimeHours = @hours, UpdatedAt = @updated WHERE Id = @id";
        cmd.Parameters.AddWithValue("@hours", hours);
        cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Games WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<string> GetDistinctDevelopers()
    {
        var result = new List<string>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT Developer FROM Games WHERE Developer IS NOT NULL AND Developer != '' ORDER BY Developer";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    private static void AddParams(SqliteCommand cmd, Game game)
    {
        cmd.Parameters.AddWithValue("@name", game.Name);
        cmd.Parameters.AddWithValue("@cover", (object?)game.CoverPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rating", game.Rating);
        cmd.Parameters.AddWithValue("@summary", (object?)game.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", game.Status.ToString());
        cmd.Parameters.AddWithValue("@developer", (object?)game.Developer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@completed", game.CompletedDate?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@hours", (object?)game.PlayTimeHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", game.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated", game.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@thumbX", game.ThumbOffsetX);
        cmd.Parameters.AddWithValue("@thumbY", game.ThumbOffsetY);
        cmd.Parameters.AddWithValue("@launch", (object?)game.LaunchPath ?? DBNull.Value);
    }

    private static Game ReadGame(SqliteDataReader r)
    {
        var rating = r.IsDBNull(3) ? 0 : r.GetDouble(3);
        var avgReview = r.IsDBNull(14) ? (double?)null : r.GetDouble(14);

        return new Game
        {
            Id = r.GetInt32(0),
            Name = r.GetString(1),
            CoverPath = r.IsDBNull(2) ? null : r.GetString(2),
            Rating = rating,
            Summary = r.IsDBNull(4) ? null : r.GetString(4),
            Status = Enum.TryParse<GameStatus>(r.GetString(5), out var s) ? s : GameStatus.Completed,
            Developer = r.IsDBNull(6) ? null : r.GetString(6),
            CompletedDate = r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7)),
            PlayTimeHours = r.IsDBNull(8) ? null : r.GetDouble(8),
            CreatedAt = DateTime.Parse(r.GetString(9)),
            UpdatedAt = DateTime.Parse(r.GetString(10)),
            ThumbOffsetX = r.IsDBNull(11) ? 0.5 : r.GetDouble(11),
            ThumbOffsetY = r.IsDBNull(12) ? 0.5 : r.GetDouble(12),
            LaunchPath = r.IsDBNull(13) ? null : r.GetString(13),
            EffectiveRating = avgReview ?? rating,
        };
    }
}
