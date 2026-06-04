namespace Mihomo.Services.Storage;

internal sealed record AppBackupProfileFile(int ProfileId, string EntryName);

internal sealed record AppBackupDocument(
    AppStateSnapshot State,
    AppBackupProfileFile[] ProfileFiles);
