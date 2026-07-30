using System;
using System.Runtime.InteropServices;

namespace FpsAiCoach
{
    public static class NativeVideoFilePicker
    {
        public static string Pick()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFilePanelWithFilters(
                "Select a Match Video",
                string.Empty,
                new[]
                {
                    "Supported Videos", "mp4,mov,webm,avi,m4v",
                    "All Files", "*"
                });
#elif UNITY_STANDALONE_WIN
            var dialog = new OpenFileName
            {
                structSize = Marshal.SizeOf<OpenFileName>(),
                filter =
                    "Supported Videos (*.mp4;*.mov;*.webm;*.avi;*.m4v)\0" +
                    "*.mp4;*.mov;*.webm;*.avi;*.m4v\0" +
                    "All Files (*.*)\0*.*\0",
                file = new string('\0', 4096),
                maxFile = 4096,
                title = "Select a Match Video",
                flags = 0x00001000 | 0x00000800
            };
            return GetOpenFileName(dialog) ? dialog.file : string.Empty;
#else
            return string.Empty;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class OpenFileName
        {
            public int structSize;
            public IntPtr dialogOwner = IntPtr.Zero;
            public IntPtr instance = IntPtr.Zero;
            public string filter;
            public string customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDirectory;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defaultExtension;
            public IntPtr customData = IntPtr.Zero;
            public IntPtr hook = IntPtr.Zero;
            public string templateName;
            public IntPtr reservedPtr = IntPtr.Zero;
            public int reservedInt;
            public int flagsExtended;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName dialog);
#endif
    }
}
