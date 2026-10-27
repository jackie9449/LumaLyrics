using System.Runtime.InteropServices;

namespace LumaLyrics.Windows.Tests;

public sealed class ProcessAudioInteropTests
{
    [Fact]
    public void NativeStructuresMatchTheX64WindowsAbi()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(18, Marshal.SizeOf<ProcessAudioInterop.WaveFormat>());
        Assert.Equal(24, Marshal.SizeOf<ProcessAudioInterop.ActivationVariant>());
        Assert.Equal(8, Marshal.OffsetOf<ProcessAudioInterop.ActivationVariant>("Size").ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<ProcessAudioInterop.ActivationVariant>("Data").ToInt32());
    }
}
