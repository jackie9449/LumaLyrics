using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaLyrics.Core;

namespace LumaLyrics.Windows;

public sealed record AppleMusicRenderSession(AudioTarget Target, string ProcessName, int State, float? Peak);
public sealed record AppleMusicAudioRoutingSnapshot(uint ActiveRenderEndpoints, IReadOnlyList<AppleMusicRenderSession> Sessions);

/// <summary>Read-only endpoint/session metadata for the already verified Apple Music package.</summary>
public static class AppleMusicAudioRouting
{
    public static AppleMusicAudioRoutingSnapshot Read(string packageFamily, string sourceAppId, CancellationToken token = default)
    {
        object? enumeratorObject = null;
        IMMDeviceCollection? devices = null;
        var rows = new List<AppleMusicRenderSession>();
        try
        {
            enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!);
            var enumerator = (IMMDeviceEnumerator)enumeratorObject!;
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out devices));
            Marshal.ThrowExceptionForHR(devices.GetCount(out uint count));
            for (uint deviceIndex = 0; deviceIndex < count; deviceIndex++)
            {
                token.ThrowIfCancellationRequested();
                IMMDevice? device = null;
                object? managerObject = null;
                IAudioSessionEnumerator? sessions = null;
                try
                {
                    Marshal.ThrowExceptionForHR(devices.Item(deviceIndex, out device));
                    Marshal.ThrowExceptionForHR(device.Activate(typeof(IAudioSessionManager2).GUID, 23, 0, out managerObject));
                    var manager = (IAudioSessionManager2)managerObject;
                    Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessions));
                    Marshal.ThrowExceptionForHR(sessions.GetCount(out int sessionCount));
                    for (int i = 0; i < sessionCount; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        object? controlObject = null;
                        try
                        {
                            Marshal.ThrowExceptionForHR(sessions.GetSession(i, out controlObject));
                            var control = (IAudioSessionControl2)controlObject;
                            Marshal.ThrowExceptionForHR(control.GetProcessId(out uint pid));
                            if (pid == 0 || pid > int.MaxValue) continue;
                            using var process = Process.GetProcessById((int)pid);
                            if (AppleMusicAudioTargetResolver.PackageFamilyOf(process) != packageFamily) continue;
                            Marshal.ThrowExceptionForHR(control.GetState(out int state));
                            float? peak = null;
                            if (controlObject is IAudioMeterInformation meter && meter.GetPeakValue(out float value) >= 0) peak = value;
                            rows.Add(new(new(pid, process.StartTime.ToUniversalTime(), packageFamily, sourceAppId), process.ProcessName, state, peak));
                        }
                        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { }
                        finally { ProcessAudioInterop.Release(controlObject); }
                    }
                }
                finally
                {
                    ProcessAudioInterop.Release(sessions); ProcessAudioInterop.Release(managerObject); ProcessAudioInterop.Release(device);
                }
            }
            return new(count, rows);
        }
        finally { ProcessAudioInterop.Release(devices); ProcessAudioInterop.Release(enumeratorObject); }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(in Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(nint session, uint flags, [MarshalAs(UnmanagedType.IUnknown)] out object control);
        [PreserveSig] int GetSimpleAudioVolume(nint session, uint flags, [MarshalAs(UnmanagedType.IUnknown)] out object volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out nint name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, in Guid context);
        [PreserveSig] int GetIconPath(out nint path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, in Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(in Guid group, in Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(nint callback);
        [PreserveSig] int UnregisterAudioSessionNotification(nint callback);
        [PreserveSig] int GetSessionIdentifier(out nint name);
        [PreserveSig] int GetSessionInstanceIdentifier(out nint name);
        [PreserveSig] int GetProcessId(out uint processId);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
}
