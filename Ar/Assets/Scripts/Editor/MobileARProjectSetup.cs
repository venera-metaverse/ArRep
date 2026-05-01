using System.Reflection;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

[InitializeOnLoad]
public static class MobileARProjectSetup
{
    private const string AndroidLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
    private const string IosLoaderType = "UnityEngine.XR.ARKit.ARKitLoader";
    private const string DefaultIosCameraUsage = "Camera access is required for augmented reality.";

    static MobileARProjectSetup()
    {
        EditorApplication.delayCall += EnsureConfigured;
    }

    private static void EnsureConfigured()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        try
        {
            XRGeneralSettingsPerBuildTarget settingsPerBuildTarget = GetOrCreateSettings();
            ConfigureBuildTarget(settingsPerBuildTarget, BuildTargetGroup.Android, AndroidLoaderType);
            ConfigureBuildTarget(settingsPerBuildTarget, BuildTargetGroup.iOS, IosLoaderType);

            if (string.IsNullOrWhiteSpace(PlayerSettings.iOS.cameraUsageDescription))
            {
                PlayerSettings.iOS.cameraUsageDescription = DefaultIosCameraUsage;
            }

            AssetDatabase.SaveAssets();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[{nameof(MobileARProjectSetup)}] XR setup was not fully applied automatically: {exception.Message}");
        }
    }

    private static XRGeneralSettingsPerBuildTarget GetOrCreateSettings()
    {
        MethodInfo getOrCreateMethod = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
            "GetOrCreate",
            BindingFlags.Static | BindingFlags.NonPublic);

        return (XRGeneralSettingsPerBuildTarget)getOrCreateMethod.Invoke(null, null);
    }

    private static void ConfigureBuildTarget(
        XRGeneralSettingsPerBuildTarget settingsPerBuildTarget,
        BuildTargetGroup buildTargetGroup,
        string loaderTypeName)
    {
        if (!settingsPerBuildTarget.HasSettingsForBuildTarget(buildTargetGroup))
        {
            settingsPerBuildTarget.CreateDefaultSettingsForBuildTarget(buildTargetGroup);
        }

        if (!settingsPerBuildTarget.HasManagerSettingsForBuildTarget(buildTargetGroup))
        {
            settingsPerBuildTarget.CreateDefaultManagerSettingsForBuildTarget(buildTargetGroup);
        }

        XRGeneralSettings generalSettings = settingsPerBuildTarget.SettingsForBuildTarget(buildTargetGroup);
        if (generalSettings == null)
        {
            return;
        }

        generalSettings.InitManagerOnStart = true;

        XRManagerSettings managerSettings = generalSettings.AssignedSettings;
        if (managerSettings == null)
        {
            return;
        }

        managerSettings.automaticLoading = true;
        managerSettings.automaticRunning = true;

        if (!XRPackageMetadataStore.IsLoaderAssigned(loaderTypeName, buildTargetGroup))
        {
            XRPackageMetadataStore.AssignLoader(managerSettings, loaderTypeName, buildTargetGroup);
        }

        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(managerSettings);
    }
}
