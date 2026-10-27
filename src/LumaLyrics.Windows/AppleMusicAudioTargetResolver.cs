using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LumaLyrics.Core;
using Microsoft.Win32.SafeHandles;

namespace LumaLyrics.Windows;

/// <summary>Only accepts a package-owned process whose AUMID equals the selected media session.</summary>
public static class AppleMusicAudioTargetResolver
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder? name);

    public static Task<AudioTarget?> ResolveAsync(PlaybackSnapshot expected, CancellationToken token)
        => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!expected.SessionId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)) return null;
            var matches = new List<AudioTarget>();
            foreach (var process in Process.GetProcessesByName("AppleMusic"))
            {
                using (process)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var identity = ReadIdentity(process);
                        if (identity is { } value && value.AppId.Equals(expected.SessionId, StringComparison.Ordinal))
                            matches.Add(new((uint)process.Id, process.StartTime.ToUniversalTime(), value.Family, expected.SessionId));
                    }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
                }
            }
            if (matches.Count != 1) return null;
            try
            {
                // The packaged renderer may be a sibling (AMPLibraryAgent), not a UI child.
                var routing = AppleMusicAudioRouting.Read(matches[0].PackageFamily, expected.SessionId, token);
                return SelectRenderTarget(matches[0], routing.Sessions);
            }
            catch (Exception error) when (error is COMException or InvalidCastException or System.ComponentModel.Win32Exception) { return null; }
        }, token);

    internal static AudioTarget? SelectRenderTarget(AudioTarget owner, IReadOnlyList<AppleMusicRenderSession> sessions)
    {
        if (string.IsNullOrWhiteSpace(owner.SourceAppId)) return null;
        var active = sessions.Where(session => session.State == 1 && session.Target.ProcessId > 0
            && session.Target.PackageFamily == owner.PackageFamily && session.Target.SourceAppId == owner.SourceAppId)
            .Select(session => session.Target).Distinct().ToArray();
        return active.Length == 1 ? active[0] : null;
    }

    internal static bool IsStillValid(AudioTarget target)
    {
        if (target.ProcessId == 0 || target.ProcessId > int.MaxValue || string.IsNullOrWhiteSpace(target.PackageFamily)
            || string.IsNullOrWhiteSpace(target.SourceAppId)) return false;
        try
        {
            using var process = Process.GetProcessById((int)target.ProcessId);
            if (process.StartTime.ToUniversalTime() != target.StartTimeUtc || PackageFamilyOf(process) != target.PackageFamily) return false;
            bool ownerFound = false;
            foreach (var owner in Process.GetProcessesByName("AppleMusic"))
            {
                using (owner)
                {
                    var identity = ReadIdentity(owner);
                    if (identity is { } value && value.Family == target.PackageFamily && value.AppId == target.SourceAppId)
                        ownerFound = true;
                }
            }
            return ownerFound;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { return false; }
    }

    internal static string? PackageFamilyOf(Process process)
    {
        uint length = 0;
        if (GetPackageFamilyName(process.SafeHandle, ref length, null) != 122 || length is 0 or > 1024) return null;
        var family = new StringBuilder((int)length);
        return GetPackageFamilyName(process.SafeHandle, ref length, family) == 0 ? family.ToString() : null;
    }

    static (string AppId, string Family)? ReadIdentity(Process process)
    {
        uint length = 0;
        if (GetApplicationUserModelId(process.SafeHandle, ref length, null) != 122 || length is 0 or > 1024) return null;
        var appId = new StringBuilder((int)length);
        if (GetApplicationUserModelId(process.SafeHandle, ref length, appId) != 0) return null;
        return PackageFamilyOf(process) is { } family ? (appId.ToString(), family) : null;
    }
}
