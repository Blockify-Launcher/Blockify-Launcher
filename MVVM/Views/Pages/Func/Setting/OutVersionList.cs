using BlockifyLib.Launcher.Version;

namespace BlockifyLauncher.MVVM.Views.Pages.Func.Setting
{
    public class OutVersionList
    {
        public static string ToString(ProfileConverter.VersionType type)
        {
            switch (type)
            {
                case ProfileConverter.VersionType.OldAlpha:
                    return "Alpha";
                case ProfileConverter.VersionType.OldBeta:
                    return "Beta";
                case ProfileConverter.VersionType.Snapshot:
                    return "Snapshot";
                case ProfileConverter.VersionType.Release:
                    return "Release";
                default:
                    return "Custom";
            }
        }

        public string ToStringOrig(ProfileConverter.VersionType type)
            => ProfileConverter.ToString(type);

        public static List<ProfileConverter.VersionType> GetVersionList()
        {
            List<ProfileConverter.VersionType> _listVersion
                = new List<ProfileConverter.VersionType>();

            foreach (ProfileConverter.VersionType item in Enum.GetValues(
                typeof(ProfileConverter.VersionType)))
                _listVersion.Add(item);

            return _listVersion;
        }
    }
}