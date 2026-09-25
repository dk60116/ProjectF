using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectF.Editor.Diagnostics
{
    internal static class MemorySnapshotDevelopmentBuild
    {
        [MenuItem("ProjectF/Diagnostics/Build Memory Snapshot Player")]
        private static void Build()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            if (scenes.Count == 0)
            {
                throw new InvalidOperationException("Enable at least one scene in Build Profiles before building.");
            }

            string executablePath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "Build_PC_MemoryProfile", "ProjectF.exe"));
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = executablePath,
                target = BuildTarget.StandaloneWindows64,
                // MemoryProfiler.TakeSnapshot needs ENABLE_PROFILER. Do not autoconnect
                // to the Editor, because that streams snapshots there instead of to disk.
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Memory snapshot Player build failed: {report.summary.result}");
            }

            Debug.Log($"Memory snapshot Player built: {executablePath}");
        }
    }
}
