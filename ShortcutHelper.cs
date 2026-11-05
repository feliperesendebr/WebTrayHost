using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WebTrayHost;

internal static class ShortcutHelper
{
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);

    private static readonly Guid FmtidAppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint PidAppUserModelId = 5;

    public static void CreateOrUpdateShortcut(string shortcutTitle, string targetExePath, string workingDir, string? iconPath, string aumid)
    {
        try
        {
            string programsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string shortcutPath = Path.Combine(programsFolder, $"{shortcutTitle}.lnk");

            var shellLink = (IShellLinkW)new ShellLink();
            shellLink.SetPath(targetExePath);
            shellLink.SetWorkingDirectory(workingDir);

            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                shellLink.SetIconLocation(iconPath, 0);
            }

            var propertyStore = (IPropertyStore)shellLink;
            var key = new PROPERTYKEY { fmtid = FmtidAppUserModel, pid = PidAppUserModelId };
            var pv = new PROPVARIANT();

            try
            {
                pv.SetString(aumid);
                propertyStore.SetValue(ref key, ref pv);
                propertyStore.Commit();
            }
            finally
            {
                pv.Clear();
            }

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebTrayHost] Erro ao registrar atalho AUMID: {ex.Message}");
        }
    }
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);
    void GetAt(uint iProp, out PROPERTYKEY pkey);
    void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
    void Commit();
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    void IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(2)] public ushort wReserved1;
    [FieldOffset(4)] public ushort wReserved2;
    [FieldOffset(6)] public ushort wReserved3;
    [FieldOffset(8)] public IntPtr pwszVal;

    public void SetString(string val)
    {
        vt = 31; // VT_LPWSTR
        pwszVal = Marshal.StringToCoTaskMemUni(val);
    }

    public void Clear()
    {
        if (vt == 31 && pwszVal != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(pwszVal);
            pwszVal = IntPtr.Zero;
        }
        vt = 0;
    }
}
