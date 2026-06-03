using Microsoft.Data.Sqlite;

namespace Mihomo.Services.Storage;

public sealed class AppStateStore
{
    private const string SchemaVersion = "1";
    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public AppStateStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task<AppStateSnapshot> LoadAsync(AppStateSnapshot defaults, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var settings = await LoadSettingsAsync(connection, cancellationToken);
            var profiles = await LoadProfilesCoreAsync(connection, cancellationToken);
            var selectedProfileId = profiles.FirstOrDefault(profile => profile.IsSelected)?.Id;

            return defaults with
            {
                HomeDirectory = GetSetting(settings, "home_directory", defaults.HomeDirectory),
                ConfigPath = GetSetting(settings, "config_path", defaults.ConfigPath),
                SubscriptionUrl = GetSetting(settings, "subscription_url", defaults.SubscriptionUrl),
                MixedPort = GetSetting(settings, "mixed_port", defaults.MixedPort),
                EnableTun = GetBool(settings, "enable_tun", defaults.EnableTun),
                EnableIpv6 = GetBool(settings, "enable_ipv6", defaults.EnableIpv6),
                DnsHijacking = GetBool(settings, "dns_hijacking", defaults.DnsHijacking),
                SystemProxy = GetBool(settings, "system_proxy", defaults.SystemProxy),
                Stack = GetSetting(settings, "stack", defaults.Stack),
                RouteAddressCsv = GetSetting(settings, "route_address_csv", defaults.RouteAddressCsv),
                IsDarkTheme = GetBool(settings, "is_dark_theme", defaults.IsDarkTheme),
                CurrentProfileId = selectedProfileId ?? GetInt(settings, "current_profile_id", defaults.CurrentProfileId),
                Profiles = profiles
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAppStateAsync(AppStateSnapshot state, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await UpsertSettingAsync(connection, transaction, "home_directory", state.HomeDirectory, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "config_path", state.ConfigPath, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "subscription_url", state.SubscriptionUrl, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "mixed_port", state.MixedPort, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "enable_tun", ToValue(state.EnableTun), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "enable_ipv6", ToValue(state.EnableIpv6), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "dns_hijacking", ToValue(state.DnsHijacking), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "system_proxy", ToValue(state.SystemProxy), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "stack", state.Stack, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "route_address_csv", state.RouteAddressCsv, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "is_dark_theme", ToValue(state.IsDarkTheme), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "current_profile_id", state.CurrentProfileId?.ToString() ?? string.Empty, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StoredConfigProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            return await LoadProfilesCoreAsync(connection, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredConfigProfile> UpsertProfileAsync(StoredConfigProfile profile, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            if (profile.IsSelected)
            {
                await ClearSelectedProfilesAsync(connection, transaction, cancellationToken);
            }

            StoredConfigProfile savedProfile;
            if (profile.Id > 0)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = (SqliteTransaction)transaction;
                update.CommandText =
                    """
                    UPDATE profiles
                    SET type = $type,
                        label = $label,
                        file_path = $filePath,
                        url = $url,
                        last_update_unix_ms = $lastUpdate,
                        sort_order = $sortOrder,
                        selected = $selected
                    WHERE id = $id;
                    """;
                AddProfileParameters(update, profile);
                update.Parameters.AddWithValue("$id", profile.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
                savedProfile = profile;
            }
            else
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText =
                    """
                    INSERT INTO profiles (type, label, file_path, url, last_update_unix_ms, sort_order, selected)
                    VALUES ($type, $label, $filePath, $url, $lastUpdate, $sortOrder, $selected)
                    RETURNING id;
                    """;
                AddProfileParameters(insert, profile);
                var id = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
                savedProfile = profile with { Id = id };
            }

            if (savedProfile.IsSelected)
            {
                await UpsertSettingAsync(
                    connection,
                    transaction,
                    "current_profile_id",
                    savedProfile.Id.ToString(),
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return savedProfile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SelectProfileAsync(int profileId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await ClearSelectedProfilesAsync(connection, transaction, cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "UPDATE profiles SET selected = 1 WHERE id = $id;";
            command.Parameters.AddWithValue("$id", profileId);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await UpsertSettingAsync(connection, transaction, "current_profile_id", profileId.ToString(), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteProfileAsync(int profileId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM profiles WHERE id = $id;";
            command.Parameters.AddWithValue("$id", profileId);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await UpsertSettingAsync(connection, transaction, "current_profile_id", string.Empty, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
            await ExecuteAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS app_settings (
                    key TEXT PRIMARY KEY NOT NULL,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS profiles (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    type TEXT NOT NULL,
                    label TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    url TEXT NOT NULL,
                    last_update_unix_ms INTEGER NULL,
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    selected INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS idx_profiles_sort_order ON profiles(sort_order, id);
                """,
                cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await UpsertSettingAsync(connection, transaction, "schema_version", SchemaVersion, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<Dictionary<string, string>> LoadSettingsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM app_settings;";

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }

        return settings;
    }

    private static async Task<IReadOnlyList<StoredConfigProfile>> LoadProfilesCoreAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, type, label, file_path, url, last_update_unix_ms, sort_order, selected
            FROM profiles
            ORDER BY sort_order, id;
            """;

        var profiles = new List<StoredConfigProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTimeOffset? lastUpdateDate = null;
            if (!reader.IsDBNull(5))
            {
                lastUpdateDate = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)).ToLocalTime();
            }

            profiles.Add(
                new StoredConfigProfile(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    lastUpdateDate,
                    reader.GetInt32(6),
                    reader.GetInt32(7) != 0));
        }

        return profiles;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertSettingAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO app_settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ClearSelectedProfilesAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE profiles SET selected = 0;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddProfileParameters(SqliteCommand command, StoredConfigProfile profile)
    {
        command.Parameters.AddWithValue("$type", profile.Type);
        command.Parameters.AddWithValue("$label", profile.Label);
        command.Parameters.AddWithValue("$filePath", profile.FilePath);
        command.Parameters.AddWithValue("$url", profile.Url);
        command.Parameters.AddWithValue(
            "$lastUpdate",
            profile.LastUpdateDate?.ToUnixTimeMilliseconds() is { } value ? value : DBNull.Value);
        command.Parameters.AddWithValue("$sortOrder", profile.SortOrder);
        command.Parameters.AddWithValue("$selected", profile.IsSelected ? 1 : 0);
    }

    private static string GetSetting(Dictionary<string, string> settings, string key, string fallback)
    {
        return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static bool GetBool(Dictionary<string, string> settings, string key, bool fallback)
    {
        return settings.TryGetValue(key, out var value)
            ? value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            : fallback;
    }

    private static int? GetInt(Dictionary<string, string> settings, string key, int? fallback)
    {
        return settings.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static string ToValue(bool value)
    {
        return value ? "1" : "0";
    }
}
