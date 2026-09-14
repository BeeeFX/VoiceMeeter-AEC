using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VoiceMeeterAEC;

internal static class VoiceMeeterLabels
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SimpleCall();

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int GetStringCall(string parameter, StringBuilder value);

    public static string?[] TryRead(MixerLayout layout)
    {
        var labels = new string?[layout.StripCount];
        if (!MixerLayout.IsRunning(layout)) return labels;

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VB", "Voicemeeter", "VoicemeeterRemote64.dll");
        if (!File.Exists(path)) return labels;

        nint library = 0;
        SimpleCall? logout = null;
        var loggedIn = false;
        try
        {
            library = NativeLibrary.Load(path);
            var login = Marshal.GetDelegateForFunctionPointer<SimpleCall>(NativeLibrary.GetExport(library, "VBVMR_Login"));
            logout = Marshal.GetDelegateForFunctionPointer<SimpleCall>(NativeLibrary.GetExport(library, "VBVMR_Logout"));
            var getString = Marshal.GetDelegateForFunctionPointer<GetStringCall>(NativeLibrary.GetExport(library, "VBVMR_GetParameterStringA"));
            loggedIn = login() >= 0;
            if (!loggedIn) return labels;

            for (var strip = 0; strip < labels.Length; strip++)
            {
                var value = new StringBuilder(512);
                if (getString($"Strip[{strip}].Label", value) == 0)
                {
                    var label = value.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(label)) labels[strip] = label;
                }
            }
        }
        catch
        {
            // Friendly labels are optional; generic VoiceMeeter names remain available.
        }
        finally
        {
            if (loggedIn) try { logout?.Invoke(); } catch { }
            if (library != 0) NativeLibrary.Free(library);
        }
        return labels;
    }
}
