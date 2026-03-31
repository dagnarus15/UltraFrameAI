using System.Runtime.InteropServices;

namespace UltraFrameAI;

// Native encoder ABI seam for future FFmpeg API integration.
// This file intentionally contains interop declarations only.
internal static class NativeFrameEncoderApi
{
    private const string LibraryName = "UltraFrameAI.Encoder.Native";

    static NativeFrameEncoderApi()
    {
        NativeLibraryResolver.EnsureInstalled();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OpenConfig
    {
        public int Width;
        public int Height;
        public int Channels;
        public int FpsNum;
        public int FpsDen;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string CodecName;

        [MarshalAs(UnmanagedType.LPUTF8Str)]
        public string OutputPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FramePacket
    {
        public nint Data;
        public nuint Size;
        public long PtsNum;
        public long PtsDen;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ufe_session_create")]
    internal static extern nint CreateSession();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ufe_session_destroy")]
    internal static extern void DestroySession(nint session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ufe_session_open")]
    internal static extern int Open(nint session, ref OpenConfig config);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ufe_session_submit_frame")]
    internal static extern int SubmitFrame(nint session, ref FramePacket frame);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ufe_session_submit_timestamp")]
    internal static extern int SubmitTimestamp(nint session, long ptsNum, long ptsDen);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ufe_session_flush")]
    internal static extern int Flush(nint session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ufe_session_close")]
    internal static extern int Close(nint session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ufe_session_get_last_error")]
    internal static extern nint GetLastErrorUtf8(nint session);
}
