#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Sportland.Sports.Dodgeball.EditorTools
{
    /// <summary>
    /// One-click builder for the shareable Dodgeball player.
    ///
    ///   Sportland > Build > Windows (Ctrl/Cmd + Shift + B)
    ///     Outputs <projectRoot>/Builds/Windows/Dodgeball.exe (+ _Data folder).
    ///     Zip the whole Builds/Windows directory to share.
    ///
    /// The scene list passed to BuildPipeline is hard-coded to Dodgeball.unity,
    /// so the build doesn't depend on the project's File > Build Settings list
    /// being kept in sync.
    /// </summary>
    public static class DodgeballBuilder
    {
        private const string SceneAsset  = "Assets/Scenes/Dodgeball.unity";
        private const string WindowsDir  = "Builds/Windows";
        private const string WindowsExe  = "Dodgeball.exe";

        [MenuItem("Sportland/Build/Windows %#b")]
        public static void BuildWindows()
        {
            if (!File.Exists(SceneAsset))
            {
                Debug.LogError($"[DodgeballBuilder] Scene not found at {SceneAsset}; nothing to build.");
                return;
            }

            if (!Directory.Exists(WindowsDir)) Directory.CreateDirectory(WindowsDir);
            string output = Path.Combine(WindowsDir, WindowsExe);

            var options = new BuildPlayerOptions
            {
                scenes           = new[] { SceneAsset },
                locationPathName = output,
                target           = BuildTarget.StandaloneWindows64,
                targetGroup      = BuildTargetGroup.Standalone,
                options          = BuildOptions.None,
            };

            Debug.Log($"[DodgeballBuilder] Building {output} (scene: {SceneAsset}) ...");
            BuildReport report  = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                double mb = summary.totalSize / 1024.0 / 1024.0;
                Debug.Log($"[DodgeballBuilder] Build OK ({mb:F1} MB, {summary.totalTime}). " +
                          $"Zip {WindowsDir}/ and share.");
                EditorUtility.RevealInFinder(output);
            }
            else
            {
                Debug.LogError($"[DodgeballBuilder] Build {summary.result} — " +
                               $"{summary.totalErrors} errors, {summary.totalWarnings} warnings.");
            }
        }
    }
}
#endif
