using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace NeoChat.Windows.Core.Storage;

/// <summary>
/// Owns the one SQLite file the app persists into. The schema is not restated in C#: the frozen
/// <c>spec/schema/v1.sql</c> is embedded verbatim and executed on first launch, and later launches
/// recognise an existing database by reading <c>schema_meta.schema_version</c> instead of re-running
/// the script. Every PRAGMA the DDL header lists as mandatory is applied to the connection before any
/// statement runs.
/// </summary>
public sealed class NeoChatDatabase : IDisposable
{
    /// <summary>The only schema version this build understands (DDL <c>schema_meta.schema_version</c>).</summary>
    public const string SchemaVersion = "1";

    public const string SchemaResourceName = "NeoChat.Windows.Core.Storage.v1.sql";

    private const string DeviceIdSettingKey = "sync.device_id";

    private readonly SqliteConnection _connection;
    private bool _disposed;

    private NeoChatDatabase(string path, SqliteConnection connection, bool created, string deviceId)
    {
        Path = path;
        _connection = connection;
        Created = created;
        DeviceId = deviceId;
    }

    /// <summary><c>%LOCALAPPDATA%\NeoChat\neochat.db</c>, the location the task fixes.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NeoChat",
        "neochat.db");

    public string Path { get; }

    public string DeviceId { get; }

    /// <summary>True when this call created the file and applied the DDL.</summary>
    public bool Created { get; }

    public SqliteConnection Connection => _disposed
        ? throw new ObjectDisposedException(nameof(NeoChatDatabase))
        : _connection;

    public static NeoChatDatabase Open(string? path = null)
    {
        var resolved = string.IsNullOrWhiteSpace(path) ? DefaultPath : System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(resolved);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = resolved,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // One long-lived connection: pooling would keep the file handle alive after Dispose and
            // make "close and reopen" checks lie about what reached the disk.
            Pooling = false,
        }.ToString());
        connection.Open();
        ApplyPragmas(connection);

        try
        {
            var created = ApplySchema(connection);
            var deviceId = ReadOrCreateDeviceId(connection);
            return new NeoChatDatabase(resolved, connection, created, deviceId);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Marks every message left in <c>streaming</c> as <c>aborted</c>. A process killed mid-stream never
    /// got to write its terminal status; the text it had already persisted stays untouched, and this
    /// sweep is what makes the next launch report the turn as interrupted instead of still running.
    /// </summary>
    public static int AbortOrphanStreaming(SqliteConnection connection, long now)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var ids = new List<string>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT id FROM message WHERE status = 'streaming' AND deleted_at IS NULL";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        foreach (var id in ids)
        {
            using var update = connection.CreateCommand();
            update.CommandText =
                "UPDATE message SET status = 'aborted', updated_at = $now, sync_version = sync_version + 1, rev = $rev " +
                "WHERE id = $id AND status = 'streaming'";
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$rev", NewRevision());
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }

        return ids.Count;
    }

    public static string NewRevision() => Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        // spec/schema/v1.sql header: "缺一即为缺陷".
        Execute(connection, "PRAGMA journal_mode = WAL");
        Execute(connection, "PRAGMA foreign_keys = ON");
        Execute(connection, "PRAGMA synchronous = NORMAL");
        Execute(connection, "PRAGMA busy_timeout = 5000");
        Execute(connection, "PRAGMA temp_store = MEMORY");
    }

    private static bool ApplySchema(SqliteConnection connection)
    {
        var version = ReadSchemaVersion(connection);
        if (version is not null)
        {
            if (!string.Equals(version, SchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "database schema_version is " + version + " but this build only reads " + SchemaVersion);
            }

            return false;
        }

        var ddl = LoadSchemaSql();
        var checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ddl)));

        using (var transaction = connection.BeginTransaction())
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = ddl;
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        var integrityOk = string.Equals(Scalar(connection, "PRAGMA integrity_check"), "ok", StringComparison.Ordinal);
        using (var record = connection.CreateCommand())
        {
            record.CommandText =
                "INSERT INTO migration(name, checksum, applied_at, snapshot_path, integrity_ok) " +
                "VALUES ('v1.sql', $checksum, $now, NULL, $ok)";
            record.Parameters.AddWithValue("$checksum", checksum);
            record.Parameters.AddWithValue("$now", NowMilliseconds());
            record.Parameters.AddWithValue("$ok", integrityOk ? 1 : 0);
            record.ExecuteNonQuery();
        }

        if (!integrityOk)
        {
            throw new InvalidOperationException("freshly created database failed PRAGMA integrity_check");
        }

        return true;
    }

    private static string? ReadSchemaVersion(SqliteConnection connection)
    {
        var exists = Scalar(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_meta'");
        if (!string.Equals(exists, "1", StringComparison.Ordinal))
        {
            return null;
        }

        return Scalar(connection, "SELECT value FROM schema_meta WHERE key = 'schema_version'");
    }

    private static string ReadOrCreateDeviceId(SqliteConnection connection)
    {
        var existing = Scalar(
            connection,
            "SELECT value_json FROM app_setting WHERE key = '" + DeviceIdSettingKey + "'");
        if (existing is not null)
        {
            var parsed = JsonNode.Parse(existing);
            if (parsed is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0)
            {
                return text;
            }
        }

        var created = Uuid7.NewId();
        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO app_setting(key, value_json, updated_at) VALUES ($key, $value, $now) " +
            "ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at";
        insert.Parameters.AddWithValue("$key", DeviceIdSettingKey);
        insert.Parameters.AddWithValue("$value", JsonValue.Create(created).ToJsonString());
        insert.Parameters.AddWithValue("$now", NowMilliseconds());
        insert.ExecuteNonQuery();
        return created;
    }

    private static string LoadSchemaSql()
    {
        using var stream = typeof(NeoChatDatabase).Assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException("embedded schema resource missing: " + SchemaResourceName);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public static long NowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
