using MVR.FileManagementSecure;
using MVR.Hub;

namespace everlaster
{
    public class PackageObj
    {
        public readonly string name;
        public readonly string groupName;
        public readonly string versionStr;
        public readonly bool requireLatest;
        public string error { get; private set; }
        public int version = -1;
        public bool exists;
        public readonly int depth;
        public HubResourcePackageUI packageUI;
        public HubResourcePackage connectedItem;

        public PackageObj(string name, string[] parts, int depth)
        {
            this.name = name;
            groupName = $"{parts[0]}.{parts[1]}";
            versionStr = parts[2];
            requireLatest = versionStr == "latest";
            if(!requireLatest && !int.TryParse(versionStr, out version))
            {
                error = $"Invalid version: {versionStr}";
            }

            this.depth = depth;
            exists = FileManagerSecure.PackageExists(name);
        }

        public bool CheckExists() => exists = FileManagerSecure.PackageExists(name);

        public override string ToString() =>
            $"\n name={name}\n groupName={groupName}\n versionStr={versionStr}" +
            $"\n requireLatest={requireLatest}\n version={version}\n exists={exists}";

        public void RegisterHubItem(HubResourcePackageUI packageUI, HubResourcePackage connectedItem, int latestVersion)
        {
            if(packageUI == null || connectedItem == null)
            {
                error = "Failed to register hub item";
                return;
            }

            this.packageUI = packageUI;
            this.connectedItem = connectedItem;
            if(requireLatest)
            {
                version = latestVersion;
                if(version == -1)
                {
                    error = "Failed to determine latest version";
                }
            }
        }
    }
}
