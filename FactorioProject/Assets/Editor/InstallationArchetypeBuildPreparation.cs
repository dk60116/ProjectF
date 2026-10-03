using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ProjectF.Editor.MapObjects
{
    internal sealed class InstallationArchetypeBuildPreparation : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report) => MapObjectArchetypeBaker.RefreshArchetypesForBuild();
    }
}
