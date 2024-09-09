using MVR.FileManagementSecure;
using MVR.Hub;

namespace everlaster
{
    public class PackageObj
    {
        public readonly string name;
        public readonly string groupName;
        public readonly bool requireLatest;
        public string versionError { get; }
        public string hubItemError { get; private set; }

        // ReSharper disable once NotAccessedField.Global
        public int version = -1;
        public bool exists { get; private set; }
        public bool existsAndIsValid => exists && versionError == null;
        public readonly bool isSubDependency;
        public HubResourcePackageUI packageUI;
        public HubResourcePackage connectedItem;

        public HubResourcePackage.DownloadStartCallback storeStartCallback;
        public HubResourcePackage.DownloadCompleteCallback storeCompleteCallback;
        public HubResourcePackage.DownloadErrorCallback storeErrorCallback;
        public bool downloadStarted;
        public bool downloadComplete;
        public string downloadError;

        public PackageObj(string name, string[] parts, bool isSubDependency)
        {
            this.name = name;
            groupName = $"{parts[0]}.{parts[1]}";
            string versionStr = parts[2];
            requireLatest = versionStr == "latest";
            if(!requireLatest && !int.TryParse(versionStr, out version))
            {
                versionError = $"Invalid version: {versionStr}";
            }

            this.isSubDependency = isSubDependency;
            exists = FileManagerSecure.PackageExists(name);
        }

        public void SyncExists() => exists = FileManagerSecure.PackageExists(name);

        public void RegisterHubItem(HubResourcePackageUI packageUI, HubResourcePackage connectedItem, int latestVersion)
        {
            if(packageUI == null || connectedItem == null)
            {
                if(requireLatest && exists)
                {
                    // assumed that found package is already the latest available on the Hub
                }
                else
                {
                    hubItemError = "Failed to register hub item";
                }
            }
            else
            {
                this.packageUI = packageUI;
                this.connectedItem = connectedItem;
                if(requireLatest)
                {
                    version = latestVersion;
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
