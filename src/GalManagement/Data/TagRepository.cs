using Microsoft.Data.Sqlite;

namespace GalManagement.Data;

/// <summary>标签的数据访问(多对多:Tags + GameTags)。</summary>
public class TagRepository
{
    private readonly Database _db;

    public TagRepository(Database db) => _db = db;

    /// <summary>在用标签(至少关联一个游戏),按使用次数降序、名称升序(用于建议与筛选列表)。</summary>
    public List<string> GetAllOrdered()
    {
        var result = new List<string>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Name FROM Tags t
            LEFT JOIN GameTags gt ON gt.TagId = t.Id
            GROUP BY t.Id, t.Name
            HAVING COUNT(gt.GameId) > 0
            ORDER BY COUNT(gt.GameId) DESC, t.Name COLLATE NOCASE ASC
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>在用标签及其游戏数,按数量降序、名称升序(统计页用)。</summary>
    public List<(string Name, int Count)> GetUsageCounts()
    {
        var result = new List<(string, int)>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Name, COUNT(gt.GameId) FROM Tags t
            JOIN GameTags gt ON gt.TagId = t.Id
            GROUP BY t.Id, t.Name
            ORDER BY COUNT(gt.GameId) DESC, t.Name COLLATE NOCASE ASC
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add((reader.GetString(0), reader.GetInt32(1)));
        return result;
    }

    /// <summary>单个游戏的标签。</summary>
    public List<string> GetForGame(int gameId)
    {
        var result = new List<string>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Name FROM Tags t
            JOIN GameTags gt ON gt.TagId = t.Id
            WHERE gt.GameId = @g
            ORDER BY t.Name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@g", gameId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>批量取多个游戏的标签(库列表用,避免 N+1)。</summary>
    public Dictionary<int, List<string>> GetForGames(IEnumerable<int> gameIds)
    {
        var ids = gameIds.Distinct().ToList();
        var map = ids.ToDictionary(id => id, _ => new List<string>());
        if (ids.Count == 0)
            return map;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        var names = ids.Select((_, i) => $"@g{i}").ToList();
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(names[i], ids[i]);

        cmd.CommandText = $"""
            SELECT gt.GameId, t.Name FROM GameTags gt
            JOIN Tags t ON t.Id = gt.TagId
            WHERE gt.GameId IN ({string.Join(",", names)})
            ORDER BY gt.GameId, t.Name COLLATE NOCASE
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            map[reader.GetInt32(0)].Add(reader.GetString(1));
        return map;
    }

    /// <summary>去首尾空白、去空、忽略大小写去重。</summary>
    public static List<string> Normalize(IEnumerable<string> tags) =>
        tags.Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>原子替换某游戏的标签。</summary>
    public void SetForGame(int gameId, IEnumerable<string> tags)
    {
        var normalized = Normalize(tags);

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM GameTags WHERE GameId = @g";
            del.Parameters.AddWithValue("@g", gameId);
            del.ExecuteNonQuery();
        }

        foreach (var name in normalized)
        {
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = "INSERT OR IGNORE INTO Tags (Name) VALUES (@n)";
                ins.Parameters.AddWithValue("@n", name);
                ins.ExecuteNonQuery();
            }

            using (var link = conn.CreateCommand())
            {
                link.Transaction = tx;
                link.CommandText = """
                    INSERT OR IGNORE INTO GameTags (GameId, TagId)
                    VALUES (@g, (SELECT Id FROM Tags WHERE Name = @n))
                    """;
                link.Parameters.AddWithValue("@g", gameId);
                link.Parameters.AddWithValue("@n", name);
                link.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    /// <summary>删除某游戏的全部标签关联(游戏删除时清理)。</summary>
    public void DeleteForGame(int gameId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM GameTags WHERE GameId = @g";
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.ExecuteNonQuery();
    }
}
