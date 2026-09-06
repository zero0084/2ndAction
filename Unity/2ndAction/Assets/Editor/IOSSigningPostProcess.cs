using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

// Runs automatically right after Unity finishes generating the iOS Xcode
// project (whether via BuildIOS.Build or the normal Build Settings window),
// and bakes in the manual-signing configuration that was previously being
// re-entered by hand in Xcode's Signing & Capabilities tab every single
// time the project got regenerated (which wipes that tab back to defaults).
// With this in place, the freshly-exported project already has the right
// team/profile/certificate selected, so that manual step is no longer
// needed before Archive -> Distribute App.
public static class IOSSigningPostProcess
{
    // Update these three if the certificate/profile/team ever get
    // recreated under a different name on developer.apple.com.
    const string DevelopmentTeamId = "7AU58HR6MU";
    const string ProvisioningProfileName = "2ndAction";
    const string CodeSignIdentity = "Apple Development";

    [PostProcessBuild(1)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);

        // Only the main app target needs this - UnityFramework and the
        // test target are left alone, matching how Xcode already resolved
        // them ("No profile required") when this was set up by hand.
        string mainTarget = proj.GetUnityMainTargetGuid();
        proj.SetBuildProperty(mainTarget, "CODE_SIGN_STYLE", "Manual");
        proj.SetBuildProperty(mainTarget, "DEVELOPMENT_TEAM", DevelopmentTeamId);
        proj.SetBuildProperty(mainTarget, "PROVISIONING_PROFILE_SPECIFIER", ProvisioningProfileName);
        proj.SetBuildProperty(mainTarget, "CODE_SIGN_IDENTITY", CodeSignIdentity);
        proj.SetBuildProperty(mainTarget, "CODE_SIGN_IDENTITY[sdk=iphoneos*]", CodeSignIdentity);

        proj.WriteToFile(projPath);
    }
}
