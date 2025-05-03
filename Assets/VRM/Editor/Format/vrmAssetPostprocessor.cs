using System;
using System.IO;
using UniGLTF;
using UnityEditor;
using UnityEngine;
using VRM.Format;

namespace VRM
{
    public class vrmAssetPostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// Current importing process, don't allow another until this is completely finished.
        /// </summary>
        private static VRMAssetImportProcessor currentProcess = null;

#if !VRM_STOP_ASSETPOSTPROCESSOR
        private const string VrmExtension = ".vrm";

		static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                if (path.FastEndsWith(VrmExtension))
                    continue;

				var unityPath = UnityPath.FromUnityPath(path);

                if (!unityPath.IsFileExists) {
                    continue;
                }

                if (unityPath.IsStreamingAsset)
                {
                    UniGLTFLogger.Log($"Skip StreamingAssets: {path}");
                    continue;
                }

                var ext = Path.GetExtension(path).ToLower();
                if (ext == VrmExtension)
                {
                    try
                    {
                        ImportVrm(unityPath);
                    }
                    catch (NotVrm0Exception)
                    {
                        // is not vrm0
                    }
                }
            }
        }
#endif

        static void ImportVrm(UnityPath vrmPath)
        {
            if (!vrmPath.IsUnderWritableFolder)
            {
                throw new Exception();
            }

            var prefabPath = vrmPath.Parent.Child(vrmPath.FileNameWithoutExtension + ".prefab");

            ImportVrmAndCreatePrefab(vrmPath.FullPath, prefabPath);
        }

        public static void ImportVrmAndCreatePrefab(string vrmPath, UnityPath prefabPath)
        {
            if (!prefabPath.IsUnderWritableFolder)
            {
                UniGLTFLogger.Warning($"out of Asset or writable Packages folder: {prefabPath}");
                return;
            }

            if (currentProcess != null && !currentProcess.IsFinished)
            {
                UniGLTFLogger.Warning($"Unable to import as another import is still in progress folder: {prefabPath}");
                return;
            }

            currentProcess = new VRMAssetImportProcessor(vrmPath, prefabPath);
            currentProcess.Start();
        }

		void OnPreprocessTexture()
		{
            if (currentProcess != null)
            {
                TextureImporter textureImporter = assetImporter as TextureImporter;
                if (textureImporter != null)
                {
                    currentProcess.OnPreprocessTexture(textureImporter, assetPath, context);
                }
                else
                {
                    Debug.Log($"Importer is not for Texture [assetPath={assetPath}]");
                }
            }
        }
	}
}
