using MVR.FileManagementSecure;
using UnityEngine;
using UnityEngine.UI;

namespace everlaster
{
    public class PackageObj
    {
        public readonly string id;
        public bool exists;
        public bool pending;
        public Button downloadButton;

        public PackageObj(string id, bool exists)
        {
            this.id = id;
            this.exists = exists;
        }

        public bool CheckExists()
        {
            exists = FileManagerSecure.PackageExists(id);
            return exists;
        }
    }
}
