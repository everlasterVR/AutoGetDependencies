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
        public bool exists { get; private set; }
        public readonly int depth;
        public HubResourcePackageUI packageUI;
        public HubResourcePackage connectedItem;

        public HubResourcePackage.DownloadStartCallback storeStartCallback;
        public HubResourcePackage.DownloadCompleteCallback storeCompleteCallback;
        public HubResourcePackage.DownloadErrorCallback storeErrorCallback;
        public bool downloadStarted;
        public bool downloadComplete;
        public string downloadError;

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

        public void CheckExists() => exists = FileManagerSecure.PackageExists(name);

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

        public void CleanupCallbacks()
        {
            if(connectedItem == null)
            {
                return;
            }

            if(storeCompleteCallback != null)
                connectedItem.downloadStartCallback -= storeStartCallback;
            if(storeCompleteCallback != null)
                connectedItem.downloadCompleteCallback -= storeCompleteCallback;
            if(storeErrorCallback != null)
                connectedItem.downloadErrorCallback -= storeErrorCallback;
        }
    }
}
