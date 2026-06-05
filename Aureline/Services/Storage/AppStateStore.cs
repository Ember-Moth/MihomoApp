using Microsoft.Data.Sqlite;

namespace Aureline.Services.Storage;

public sealed class AppStateStore
{
    private const string SchemaVersion = "1";
    private static readonly object ProviderGate = new();
    private static bool providerInitialized;

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
                AllowLan = GetBool(settings, "allow_lan", defaults.AllowLan),
                DnsHijacking = GetBool(settings, "dns_hijacking", defaults.DnsHijacking),
                SystemProxy = GetBool(settings, "system_proxy", defaults.SystemProxy),
                Stack = GetSetting(settings, "stack", defaults.Stack),
                RouteAddressCsv = GetSetting(settings, "route_address_csv", defaults.RouteAddressCsv),
                OutboundMode = GetSetting(settings, "outbound_mode", defaults.OutboundMode),
                LogLevel = GetSetting(settings, "log_level", defaults.LogLevel),
                GlobalUa = GetSetting(settings, "global_ua", defaults.GlobalUa),
                TestUrl = GetSetting(settings, "test_url", defaults.TestUrl),
                UnifiedDelay = GetBool(settings, "unified_delay", defaults.UnifiedDelay),
                TcpConcurrent = GetBool(settings, "tcp_concurrent", defaults.TcpConcurrent),
                FindProcess = GetBool(settings, "find_process", defaults.FindProcess),
                GeodataMemory = GetBool(settings, "geodata_memory", defaults.GeodataMemory),
                ExternalController = GetBool(settings, "external_controller", defaults.ExternalController),
                Locale = GetSetting(settings, "locale", defaults.Locale),
                IsDarkTheme = GetBool(settings, "is_dark_theme", defaults.IsDarkTheme),
                MinimizeOnExit = GetBool(settings, "minimize_on_exit", defaults.MinimizeOnExit),
                AutoRun = GetBool(settings, "auto_run", defaults.AutoRun),
                Hidden = GetBool(settings, "hidden", defaults.Hidden),
                AnimateTabs = GetBool(settings, "animate_tabs", defaults.AnimateTabs),
                OpenLogs = GetBool(settings, "open_logs", defaults.OpenLogs),
                CloseConnections = GetBool(settings, "close_connections", defaults.CloseConnections),
                OnlyStatisticsProxy = GetBool(settings, "only_statistics_proxy", defaults.OnlyStatisticsProxy),
                Crashlytics = GetBool(settings, "crashlytics", defaults.Crashlytics),
                AutoCheckUpdates = GetBool(settings, "auto_check_updates", defaults.AutoCheckUpdates),
                AccessControlEnabled = GetBool(settings, "access_control_enabled", defaults.AccessControlEnabled),
                AccessControlMode = GetSetting(settings, "access_control_mode", defaults.AccessControlMode),
                AccessPackageCsv = GetSetting(settings, "access_package_csv", defaults.AccessPackageCsv),
                AccessFilterSystemApps = GetBool(settings, "access_filter_system_apps", defaults.AccessFilterSystemApps),
                AccessFilterNoInternetApps = GetBool(settings, "access_filter_no_internet_apps", defaults.AccessFilterNoInternetApps),
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
            await UpsertSettingAsync(connection, transaction, "allow_lan", ToValue(state.AllowLan), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "dns_hijacking", ToValue(state.DnsHijacking), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "system_proxy", ToValue(state.SystemProxy), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "stack", state.Stack, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "route_address_csv", state.RouteAddressCsv, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "outbound_mode", state.OutboundMode, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "log_level", state.LogLevel, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "global_ua", state.GlobalUa, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "test_url", state.TestUrl, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "unified_delay", ToValue(state.UnifiedDelay), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "tcp_concurrent", ToValue(state.TcpConcurrent), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "find_process", ToValue(state.FindProcess), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "geodata_memory", ToValue(state.GeodataMemory), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "external_controller", ToValue(state.ExternalController), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "locale", state.Locale, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "is_dark_theme", ToValue(state.IsDarkTheme), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "minimize_on_exit", ToValue(state.MinimizeOnExit), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "auto_run", ToValue(state.AutoRun), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "hidden", ToValue(state.Hidden), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "animate_tabs", ToValue(state.AnimateTabs), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "open_logs", ToValue(state.OpenLogs), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "close_connections", ToValue(state.CloseConnections), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "only_statistics_proxy", ToValue(state.OnlyStatisticsProxy), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "crashlytics", ToValue(state.Crashlytics), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "auto_check_updates", ToValue(state.AutoCheckUpdates), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "access_control_enabled", ToValue(state.AccessControlEnabled), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "access_control_mode", state.AccessControlMode, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "access_package_csv", state.AccessPackageCsv, cancellationToken);
            await UpsertSettingAsync(connection, transaction, "access_filter_system_apps", ToValue(state.AccessFilterSystemApps), cancellationToken);
            await UpsertSettingAsync(connection, transaction, "access_filter_no_internet_apps", ToValue(state.AccessFilterNoInternetApps), cancellationToken);
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

    public async Task ReplaceProfilesAsync(
        IReadOnlyList<StoredConfigProfile> profiles,
        int? currentProfileId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = (SqliteTransaction)transaction;
                delete.CommandText = "DELETE FROM profiles;";
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var profile in profiles)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText =
                    """
                    INSERT INTO profiles (id, type, label, file_path, url, last_update_unix_ms, sort_order, selected)
                    VALUES ($id, $type, $label, $filePath, $url, $lastUpdate, $sortOrder, $selected);
                    """;
                insert.Parameters.AddWithValue("$id", profile.Id);
                AddProfileParameters(insert, profile);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpsertSettingAsync(
                connection,
                transaction,
                "current_profile_id",
                currentProfileId?.ToString() ?? string.Empty,
                cancellationToken);
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

            EnsureSqliteProvider();

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

    private static void EnsureSqliteProvider()
    {
        if (providerInitialized)
        {
            return;
        }

        lock (ProviderGate)
        {
            if (providerInitialized)
            {
                return;
            }

            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
            SQLitePCL.raw.FreezeProvider();
            providerInitialized = true;
        }
    }
}
