using GalManagement.Models;
using Microsoft.Data.Sqlite;

namespace GalManagement.Data;

/// <summary>游戏分项评价的数据访问(手写参数化 SQL)。</summary>
public class ReviewRepository
{
    private const string Columns = "Id, GameId, Title, Rating, Comment, CoverPath, ThumbOffsetX, ThumbOffsetY, CreatedAt, UpdatedAt";

    private readonly Database _db;

    public ReviewRepository(Database db) => _db = db;

    public List<GameReview> GetByGame(int gameId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM GameReviews WHERE GameId = @gameId ORDER BY Id";
        cmd.Parameters.AddWithValue("@gameId", gameId);

        var list = new List<GameReview>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadReview(reader));
        return list;
    }

    public int Add(GameReview review)
    {
        review.CreatedAt = DateTime.Now;
        review.UpdatedAt = review.CreatedAt;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO GameReviews (GameId, Title, Rating, Comment, CoverPath, ThumbOffsetX, ThumbOffsetY, CreatedAt, UpdatedAt)
            VALUES (@gameId, @title, @rating, @comment, @cover, @thumbX, @thumbY, @created, @updated);
            SELECT last_insert_rowid();
            """;
        AddParams(cmd, review);
        review.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return review.Id;
    }

    public void Update(GameReview review)
    {
        review.UpdatedAt = DateTime.Now;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE GameReviews SET
                Title = @title, Rating = @rating, Comment = @comment, CoverPath = @cover,
                ThumbOffsetX = @thumbX, ThumbOffsetY = @thumbY, UpdatedAt = @updated
            WHERE Id = @id;
            """;
        AddParams(cmd, review);
        cmd.Parameters.AddWithValue("@id", review.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM GameReviews WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void AddParams(SqliteCommand cmd, GameReview review)
    {
        cmd.Parameters.AddWithValue("@gameId", review.GameId);
        cmd.Parameters.AddWithValue("@title", review.Title);
        cmd.Parameters.AddWithValue("@rating", review.Rating);
        cmd.Parameters.AddWithValue("@comment", (object?)review.Comment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cover", (object?)review.CoverPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@thumbX", review.ThumbOffsetX);
        cmd.Parameters.AddWithValue("@thumbY", review.ThumbOffsetY);
        cmd.Parameters.AddWithValue("@created", review.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated", review.UpdatedAt.ToString("O"));
    }

    private static GameReview ReadReview(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        GameId = r.GetInt32(1),
        Title = r.GetString(2),
        Rating = r.IsDBNull(3) ? 0 : r.GetDouble(3),
        Comment = r.IsDBNull(4) ? null : r.GetString(4),
        CoverPath = r.IsDBNull(5) ? null : r.GetString(5),
        ThumbOffsetX = r.IsDBNull(6) ? 0.5 : r.GetDouble(6),
        ThumbOffsetY = r.IsDBNull(7) ? 0.5 : r.GetDouble(7),
        CreatedAt = DateTime.Parse(r.GetString(8)),
        UpdatedAt = DateTime.Parse(r.GetString(9)),
    };
}
