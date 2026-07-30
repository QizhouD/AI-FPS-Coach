using System.IO;
using UnityEditor;
using UnityEngine;

namespace FpsAiCoach.Editor
{
    [InitializeOnLoad]
    internal static class CoachStudioTemplateAutoRebuild
    {
        private const string TriggerPath = "Assets/Editor/RebuildCoachStudio.once";

        static CoachStudioTemplateAutoRebuild()
        {
            EditorApplication.delayCall += RebuildIfRequested;
        }

        private static void RebuildIfRequested()
        {
            if (!File.Exists(Path.GetFullPath(TriggerPath)))
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += RebuildIfRequested;
                return;
            }

            CoachStudioTemplateBuilder.CreateTemplate();
            AssetDatabase.DeleteAsset(TriggerPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Completed the requested one-time FPS Coach Studio rebuild.");
        }
    }
}
