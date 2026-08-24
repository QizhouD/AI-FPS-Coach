using System.IO;
using UnityEditor;

namespace FpsAiCoach.Editor
{
    internal static class WarRoomAssetUtility
    {
        /// <summary>Creates a project folder and any missing parents.</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent ?? "Assets", name);
        }
    }
}
