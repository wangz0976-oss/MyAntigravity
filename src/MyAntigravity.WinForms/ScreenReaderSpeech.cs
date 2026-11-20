using System.Runtime.InteropServices;

namespace MyAntigravity;

internal static class ScreenReaderSpeech
{
    private static readonly Lazy<Speak?> Zdsr = new(FindZdsr);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int Init(int channel, string name, [MarshalAs(UnmanagedType.Bool)] bool keyInterrupt);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int Speak(string text, [MarshalAs(UnmanagedType.Bool)] bool interrupt);

    internal static bool TrySpeak(string text, bool interrupt = true) =>
        !string.IsNullOrWhiteSpace(text) && Zdsr.Value?.Invoke(text, interrupt) == 0;

    private static Speak? FindZdsr()
    {
        var installed = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (installed.Length == 0)
            return null;
        var dll = Path.Combine(installed, "zdsr", "zdsr", Environment.Is64BitProcess ? "ZDSRAPI_x64.dll" : "ZDSRAPI.dll");
        if (!File.Exists(dll) || !NativeLibrary.TryLoad(dll, out var library))
            return null;

        if (NativeLibrary.TryGetExport(library, "InitTTS", out var init) &&
            NativeLibrary.TryGetExport(library, "Speak", out var speak) &&
            Marshal.GetDelegateForFunctionPointer<Init>(init)(1, "MyAntigravity", true) == 0)
        {
            return Marshal.GetDelegateForFunctionPointer<Speak>(speak);
        }
        NativeLibrary.Free(library);
        return null;
    }
}
