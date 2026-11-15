using System.Runtime.InteropServices;
using System.Text;

namespace LumaLyrics.Windows;

// Windows' offline ELS service. No lyric text leaves the machine or is persisted here.
public static class TextLanguageDetector
{
    public static string? Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        nint guid = 0, services = 0;
        var bag = new PropertyBag { Size = (nuint)Marshal.SizeOf<PropertyBag>() };
        bool recognized = false;
        try
        {
            // Bound native work and validate UTF-16 before entering native code.
            if (text.Length > 4096) text = text[..(char.IsHighSurrogate(text[4095]) ? 4095 : 4096)];
            text = text.Normalize(NormalizationForm.FormC);
            guid = Marshal.AllocHGlobal(16);
            Marshal.StructureToPtr(new Guid("CF7E00B1-909B-4D95-A8F4-611F7C377702"), guid, false);
            var options = new EnumOptions { Size = (nuint)Marshal.SizeOf<EnumOptions>(), Guid = guid };
            if (MappingGetServices(ref options, out services, out uint count) < 0 || services == 0 || count == 0) return null;
            if (MappingRecognizeText(services, text, (uint)text.Length, 0, 0, ref bag) < 0) return null;
            recognized = true;
            if (bag.Ranges == 0 || bag.Count == 0) return null;
            var range = Marshal.PtrToStructure<DataRange>(bag.Ranges);
            if (range.Data == 0 || range.DataSize < 2 || range.DataSize > 65536) return null;
            string data = Marshal.PtrToStringUni(range.Data, (int)range.DataSize / 2) ?? "";
            return data.Split('\0')[0] is { Length: > 0 } language ? language : null;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or ArgumentException or ExternalException) { return null; }
        finally
        {
            if (recognized) MappingFreePropertyBag(ref bag);
            if (services != 0) MappingFreeServices(services);
            if (guid != 0) Marshal.FreeHGlobal(guid);
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct EnumOptions
    {
        public nuint Size;
        public nint Category, InputLanguage, OutputLanguage, InputScript, OutputScript, InputContentType, OutputContentType, Guid;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyBag
    {
        public nuint Size;
        public nint Ranges;
        public uint Count;
        public nint ServiceData;
        public uint ServiceDataSize;
        public nint CallerData;
        public uint CallerDataSize;
        public nint Context;
    }
    [StructLayout(LayoutKind.Sequential)] struct DataRange
    {
        public uint Start, End;
        public nint Description;
        public uint DescriptionLength;
        public nint Data;
        public uint DataSize;
        public nint ContentType, ActionIds;
        public uint ActionsCount;
        public nint ActionDisplayNames;
    }
    [DllImport("elscore.dll")] static extern int MappingGetServices(ref EnumOptions options, out nint services, out uint count);
    [DllImport("elscore.dll", CharSet = CharSet.Unicode)] static extern int MappingRecognizeText(nint service, string text, uint length, uint index, nint options, ref PropertyBag bag);
    [DllImport("elscore.dll")] static extern int MappingFreePropertyBag(ref PropertyBag bag);
    [DllImport("elscore.dll")] static extern int MappingFreeServices(nint services);
}
