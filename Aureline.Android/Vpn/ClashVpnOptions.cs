using Android.Content;
using Aureline.Models;

namespace Aureline.Android.Vpn;

internal sealed record ClashVpnOptions(
    string HomeDirectory,
    string ConfigPath,
    int MixedPort,
    bool EnableIpv6,
    bool DnsHijacking,
    bool SystemProxy,
    string Stack,
    string RouteAddressCsv,
    bool AccessControlEnabled,
    string AccessControlMode,
    string AccessPackageCsv)
{
    private const string ExtraHomeDirectory = "homeDirectory";
    private const string ExtraConfigPath = "configPath";
    private const string ExtraMixedPort = "mixedPort";
    private const string ExtraEnableIpv6 = "enableIpv6";
    private const string ExtraDnsHijacking = "dnsHijacking";
    private const string ExtraSystemProxy = "systemProxy";
    private const string ExtraStack = "stack";
    private const string ExtraRouteAddressCsv = "routeAddressCsv";
    private const string ExtraAccessControlEnabled = "accessControlEnabled";
    private const string ExtraAccessControlMode = "accessControlMode";
    private const string ExtraAccessPackageCsv = "accessPackageCsv";

    public static ClashVpnOptions FromProfile(ClashProfile profile)
    {
        return new ClashVpnOptions(
            profile.HomeDirectory,
            profile.ConfigPath,
            profile.MixedPort,
            profile.EnableIpv6,
            profile.DnsHijacking,
            profile.SystemProxy,
            profile.Stack,
            profile.RouteAddressCsv,
            profile.AccessControlEnabled,
            profile.AccessControlMode,
            string.Join(',', profile.AccessPackageNames));
    }

    public static ClashVpnOptions FromIntent(Intent intent)
    {
        return new ClashVpnOptions(
            intent.GetStringExtra(ExtraHomeDirectory) ?? string.Empty,
            intent.GetStringExtra(ExtraConfigPath) ?? string.Empty,
            intent.GetIntExtra(ExtraMixedPort, 7890),
            intent.GetBooleanExtra(ExtraEnableIpv6, false),
            intent.GetBooleanExtra(ExtraDnsHijacking, true),
            intent.GetBooleanExtra(ExtraSystemProxy, false),
            intent.GetStringExtra(ExtraStack) ?? "system",
            intent.GetStringExtra(ExtraRouteAddressCsv) ?? "0.0.0.0/0",
            intent.GetBooleanExtra(ExtraAccessControlEnabled, false),
            intent.GetStringExtra(ExtraAccessControlMode) ?? "rejectSelected",
            intent.GetStringExtra(ExtraAccessPackageCsv) ?? string.Empty);
    }

    public void PutInto(Intent intent)
    {
        intent.PutExtra(ExtraHomeDirectory, HomeDirectory);
        intent.PutExtra(ExtraConfigPath, ConfigPath);
        intent.PutExtra(ExtraMixedPort, MixedPort);
        intent.PutExtra(ExtraEnableIpv6, EnableIpv6);
        intent.PutExtra(ExtraDnsHijacking, DnsHijacking);
        intent.PutExtra(ExtraSystemProxy, SystemProxy);
        intent.PutExtra(ExtraStack, Stack);
        intent.PutExtra(ExtraRouteAddressCsv, RouteAddressCsv);
        intent.PutExtra(ExtraAccessControlEnabled, AccessControlEnabled);
        intent.PutExtra(ExtraAccessControlMode, AccessControlMode);
        intent.PutExtra(ExtraAccessPackageCsv, AccessPackageCsv);
    }

    public IReadOnlyList<string> AccessPackageNames =>
        AccessPackageCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
