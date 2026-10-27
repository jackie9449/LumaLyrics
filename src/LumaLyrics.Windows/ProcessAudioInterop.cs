using System.Runtime.InteropServices;

namespace LumaLyrics.Windows;

// ABI declarations checked against Microsoft's mmdeviceapi.idl, audioclient.h and
// audioclientactivationparams.h. No dependency on a registered native COM server.
internal static class ProcessAudioInterop
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint SampleRate, BytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    // PROPVARIANT's BLOB is aligned at offset 8 on win-x64.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct ActivationVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint Size;
        [FieldOffset(16)] public nint Data;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int ActivateAudioInterfaceAsync(string path, in Guid iid, in ActivationVariant parameters,
        IProcessAudioActivationCompletion completion, out IProcessAudioActivationOperation operation);
    [DllImport("ole32.dll")] internal static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] internal static extern void CoUninitialize();

    internal static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint flags, long duration, long periodicity, in WaveFormat format, nint session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);
        [PreserveSig] int GetMixFormat(out nint format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(nint handle);
        [PreserveSig] int GetService(in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProcessAudioActivationOperation
{
    [PreserveSig] int GetActivateResult(out int activationResult, [MarshalAs(UnmanagedType.IUnknown)] out object? client);
}

[ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProcessAudioActivationCompletion
{
    [PreserveSig] int ActivateCompleted(IProcessAudioActivationOperation operation);
}

[ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProcessAudioAgileObject { }

/// <summary>Kept alive by the native operation until completion, including abandoned starts.</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ProcessAudioActivationCompletion : IProcessAudioActivationCompletion, IProcessAudioAgileObject
{
    readonly object gate = new();
    readonly TaskCompletionSource<ProcessAudioInterop.IAudioClient> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    nint blob;
    bool abandoned;

    internal ProcessAudioActivationCompletion(uint processId)
    {
        blob = Marshal.AllocCoTaskMem(12);
        Marshal.WriteInt32(blob, 0, 1); // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
        Marshal.WriteInt32(blob, 4, checked((int)processId));
        Marshal.WriteInt32(blob, 8, 0); // INCLUDE_TARGET_PROCESS_TREE
    }

    internal ProcessAudioInterop.IAudioClient Activate(CancellationToken token)
    {
        IProcessAudioActivationOperation? operation = null;
        try
        {
            var parameters = new ProcessAudioInterop.ActivationVariant { Type = 65, Size = 12, Data = blob };
            int result = ProcessAudioInterop.ActivateAudioInterfaceAsync("VAD\\Process_Loopback",
                typeof(ProcessAudioInterop.IAudioClient).GUID, parameters, this, out operation);
            if (result < 0) { FreeBlob(); Marshal.ThrowExceptionForHR(result); }
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(5), token).GetAwaiter().GetResult();
        }
        catch
        {
            if (operation is null) FreeBlob();
            lock (gate)
            {
                abandoned = true;
                if (completion.Task.IsCompletedSuccessfully) ProcessAudioInterop.Release(completion.Task.Result);
            }
            _ = completion.Task.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        finally { ProcessAudioInterop.Release(operation); }
    }

    public int ActivateCompleted(IProcessAudioActivationOperation operation)
    {
        object? value = null;
        try
        {
            Marshal.ThrowExceptionForHR(operation.GetActivateResult(out int result, out value));
            Marshal.ThrowExceptionForHR(result);
            if (value is not ProcessAudioInterop.IAudioClient client) throw new InvalidCastException("Audio activation returned an invalid interface.");
            lock (gate)
            {
                if (!abandoned) { completion.TrySetResult(client); value = null; }
            }
        }
        catch (Exception error) { completion.TrySetException(error); }
        finally { ProcessAudioInterop.Release(value); FreeBlob(); }
        return 0;
    }

    void FreeBlob()
    {
        nint pointer = Interlocked.Exchange(ref blob, 0);
        if (pointer != 0) Marshal.FreeCoTaskMem(pointer);
    }
}
