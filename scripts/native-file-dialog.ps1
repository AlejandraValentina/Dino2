# Native file picker helpers for external WPF smoke tests.
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class DialogWindows
{
    private delegate bool Callback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint thread, Callback action, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, Callback action, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, string text);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static IntPtr Child(IntPtr dialog, string className, int preferred)
    {
        IntPtr fallback = IntPtr.Zero, exact = IntPtr.Zero;
        EnumChildWindows(dialog, delegate(IntPtr window, IntPtr arg) {
            var cls = new StringBuilder(128);
            GetClassName(window, cls, cls.Capacity);
            if (cls.ToString() != className || !IsWindowVisible(window)) return true;
            if (GetDlgCtrlID(window) == preferred) { exact = window; return false; }
            fallback = window;
            return true;
        }, IntPtr.Zero);
        return exact == IntPtr.Zero && className == "Edit" ? fallback : exact;
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendPointer(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);
    public static void SetFilename(IntPtr edit, string value)
    {
        SendPointer(edit, 0x00B1, IntPtr.Zero, (IntPtr)(-1)); // select the filename
        SendMessage(edit, 0x00C2, (IntPtr)1, value); // replace selected text and send edit notifications
        SendPointer(GetParent(edit), 0x0111, (IntPtr)((0x0300 << 16) | GetDlgCtrlID(edit)), edit); // EN_CHANGE
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr ReadText(IntPtr window, uint message, IntPtr capacity, StringBuilder buffer);
    public static string Text(IntPtr window)
    {
        var buffer = new StringBuilder(2048);
        ReadText(window, 0x000D, (IntPtr)buffer.Capacity, buffer);
        return buffer.ToString();
    }
    public static void Confirm(IntPtr dialog, IntPtr button, int command = 1) { PostMessage(dialog, 0x0111, (IntPtr)command, button); }

    public static IntPtr[] Find(uint thread, string classPrefix = "#32770")
    {
        var handles = new List<IntPtr>();
        EnumThreadWindows(thread, delegate(IntPtr window, IntPtr arg) {
            var cls = new StringBuilder(128);
            GetClassName(window, cls, cls.Capacity);
            if (cls.ToString().StartsWith(classPrefix, StringComparison.Ordinal)) handles.Add(window);
            return true;
        }, IntPtr.Zero);
        return handles.ToArray();
    }
}
"@
