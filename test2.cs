using System;
using System.Runtime.InteropServices;
public class Test2 {
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
}
