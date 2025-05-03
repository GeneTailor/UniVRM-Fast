using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniGLTF;
using UniGLTF.Utils;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace VRM
{
    public class vrmAssetPostprocessor : AssetPostprocessor
    {
        private static ProfilerMarker s_MarkerGlbParsing = new ProfilerMarker("Glb Parsing");
        private static ProfilerMarker s_MarkerCreatePrefab = new ProfilerMarker("Create Prefab");
		private static ProfilerMarker s_MarkerConfigureTextures = new ProfilerMarker("Configure Textures");
		private static ProfilerMarker s_MarkerLoadContext = new ProfilerMarker("Load Context");
		private static ProfilerMarker s_MarkerSaveAsAsset = new ProfilerMarker("Save As Asset");

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

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            s_MarkerGlbParsing.Begin();
            var gltfData = new GlbFileParser(vrmPath).Parse();
            var vrmData = new VRMData(gltfData);
            s_MarkerGlbParsing.End();

            /// <summary>
            /// これは EditorApplication.delayCall により呼び出される。
            /// 
            /// * delayCall には UnityEngine.Object 持ち越すことができない
            /// * vrmPath のみを持ち越す
            /// 
            /// </summary>
            /// <value></value>
            Action<IEnumerable<UnityPath>> onCompleted = texturePaths =>
            {
                s_MarkerCreatePrefab.Begin();

                var map = texturePaths
                    .Select(x => x.LoadAsset<Texture>())
                    .ToDictionary(x => new SubAssetKey(x), x => x as UnityEngine.Object);

                var settings = new ImporterContextSettings();

                // 確実に Dispose するために敢えて再パースしている
                using (var context = new VRMImporterContext(vrmData, externalObjectMap: map, settings: settings))
                {
					var editor = new VRMEditorImporterContext(context, prefabPath);
					s_MarkerConfigureTextures.Begin();
                    UnityEditorUtils.AssetEditingBlock(() =>
                    {
						foreach (var textureInfo in context.TextureDescriptorGenerator.Get().GetEnumerable())
						{
							TextureImporterConfigurator.Configure(textureInfo, context.TextureFactory.ExternalTextures);
						}
					});
					s_MarkerConfigureTextures.End();

					s_MarkerLoadContext.Begin();
					var loaded = context.Load();
                    s_MarkerLoadContext.End();

					s_MarkerSaveAsAsset.Begin();
                    UnityEditorUtils.AssetEditingBlock(() =>
					{
					    editor.SaveAsAsset(loaded);
					});
					s_MarkerSaveAsAsset.End();
				}

                s_MarkerCreatePrefab.End();

                gltfData.Dispose();

                sw.Stop();

                Debug.Log($"Import complete [importMs={sw.ElapsedMilliseconds}]");
            };

            using (var context = new VRMImporterContext(vrmData))
            {
                var editor = new VRMEditorImporterContext(context, prefabPath);
                // extract texture images
                editor.ConvertAndExtractImages(onCompleted);
            }
        }

		void OnPreprocessTexture()
		{
            Debug.Log($"Texture Preprocess [assetPath={assetPath}]");

			/*if (assetPath.Contains("_bumpmap"))
			{
				TextureImporter textureImporter = (TextureImporter)assetImporter;
				textureImporter.convertToNormalmap = true;
			}*/
		}
	}
}
