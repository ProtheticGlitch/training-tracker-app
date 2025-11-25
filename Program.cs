using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TrainingTracker.DatabaseApp;

public sealed record TrainingSession(
    Guid Id,
    DateOnly Date,
    string Type,
    int DurationMinutes,
    int Intensity,
    string Notes)
{
    public string DateFormatted => Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}

public sealed record TrainingStatistics(
    int TotalSessions,
    int TotalMinutes,
    double AverageDurationMinutes,
    double AverageIntensity,
    string? MostPopularWorkout,
    int LastWeekSessions);

internal sealed class TrainingDatabase
{
    private const string DateFormat = "yyyy-MM-dd";
    private readonly string _connectionString;

    public TrainingDatabase(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    public IReadOnlyCollection<TrainingSession> GetSessions()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, date, type, duration, intensity, notes
            FROM training_sessions
            ORDER BY date DESC;
            """;

        using var reader = command.ExecuteReader();
        var sessions = new List<TrainingSession>();
        while (reader.Read())
        {
            sessions.Add(MapSession(reader));
        }

        return sessions;
    }

    public TrainingSession AddSession(DateOnly date, string type, int duration, int intensity, string notes)
    {
        var session = new TrainingSession(Guid.NewGuid(), date, type.Trim(), Math.Max(1, duration), intensity, notes.Trim());

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO training_sessions (id, date, type, duration, intensity, notes)
            VALUES ($id, $date, $type, $duration, $intensity, $notes);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$date", session.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$type", session.Type);
        command.Parameters.AddWithValue("$duration", session.DurationMinutes);
        command.Parameters.AddWithValue("$intensity", session.Intensity);
        command.Parameters.AddWithValue("$notes", session.Notes);
        command.ExecuteNonQuery();

        return session;
    }

    public bool RemoveSession(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM training_sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteNonQuery() > 0;
    }

    public TrainingStatistics BuildStatistics()
    {
        using var connection = OpenConnection();

        using var totalsCommand = connection.CreateCommand();
        totalsCommand.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(duration),0), COALESCE(AVG(duration),0), COALESCE(AVG(intensity),0)
            FROM training_sessions;
            """;
        using var totalsReader = totalsCommand.ExecuteReader();
        totalsReader.Read();

        var totalSessions = totalsReader.GetInt32(0);
        var totalMinutes = totalsReader.GetInt32(1);
        var avgDuration = totalsReader.GetDouble(2);
        var avgIntensity = totalsReader.GetDouble(3);

        string? mostPopular = null;
        if (totalSessions > 0)
        {
            using var popularCommand = connection.CreateCommand();
            popularCommand.CommandText = """
                SELECT type
                FROM training_sessions
                WHERE TRIM(type) <> ''
                GROUP BY type
                ORDER BY COUNT(*) DESC
                LIMIT 1;
                """;
            mostPopular = popularCommand.ExecuteScalar() as string;
        }

        using var lastWeekCommand = connection.CreateCommand();
        lastWeekCommand.CommandText = "SELECT COUNT(*) FROM training_sessions WHERE date >= $threshold;";
        var threshold = DateOnly.FromDateTime(DateTime.Today).AddDays(-7).ToString(DateFormat, CultureInfo.InvariantCulture);
        lastWeekCommand.Parameters.AddWithValue("$threshold", threshold);
        var lastWeekSessions = Convert.ToInt32(lastWeekCommand.ExecuteScalar() ?? 0);

        return new TrainingStatistics(
            totalSessions,
            totalMinutes,
            totalSessions == 0 ? 0 : avgDuration,
            totalSessions == 0 ? 0 : avgIntensity,
            mostPopular,
            lastWeekSessions);
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS training_sessions (
                id TEXT PRIMARY KEY,
                date TEXT NOT NULL,
                type TEXT NOT NULL,
                duration INTEGER NOT NULL,
                intensity INTEGER NOT NULL,
                notes TEXT
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static TrainingSession MapSession(SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(0));
        var date = DateOnly.ParseExact(reader.GetString(1), DateFormat, CultureInfo.InvariantCulture);
        var type = reader.GetString(2);
        var duration = reader.GetInt32(3);
        var intensity = reader.GetInt32(4);
        var notes = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
        return new TrainingSession(id, date, type, duration, intensity, notes);
    }
}
