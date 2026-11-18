using System.IO;
using UnityEngine;

namespace Aegis
{
    public static class AegisPaths
    {
        // Where points.json, points.jsonl, and aegis.config.json are looked for:
        // the project folder in the editor, and the folder holding the app in a build.
        public static string RootFolder
        {
            get
            {
                string data = Application.dataPath;
                // macOS: dataPath is Aegis.app/Contents; the files sit beside Aegis.app.
                if (Application.platform == RuntimePlatform.OSXPlayer)
                    return Path.GetFullPath(Path.Combine(data, "..", ".."));
                // Editor: dataPath is <project>/Assets. Windows/Linux: <folder>/Aegis_Data.
                return Path.GetFullPath(Path.Combine(data, ".."));
            }
        }

        public static string Root(string name)
        {
            return Path.Combine(RootFolder, name);
        }

        public static string Streaming(string name)
        {
            return Path.Combine(Application.streamingAssetsPath, name);
        }
    }
}
