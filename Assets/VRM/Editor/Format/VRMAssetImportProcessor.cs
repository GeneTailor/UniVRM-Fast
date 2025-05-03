using System.Collections.Generic;
using System.Linq;
using UniGLTF;
using UniGLTF.Utils;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace VRM.Format
{
    /// <summary>
    /// Continuation of vrmAssetPostprocessor that stores some state between steps to allow for better performance.
    /// </summary>
    internal class VRMAssetImportProcessor
    {
        private string vrmPath;
        private UnityPath prefabPath;

        private GltfData gltfData;
        private VRMData vrmData;
        private System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        private Dictionary<string, TextureDescriptor> pathToDescriptor;

        public bool IsFinished { get; private set; }

        private static ProfilerMarker s_MarkerGlbParsing = new ProfilerMarker("Glb Parsing");
        private static ProfilerMarker s_MarkerCreatePrefab = new ProfilerMarker("Create Prefab");
        private static ProfilerMarker s_MarkerConfigureTextures = new ProfilerMarker("Configure Textures");
        private static ProfilerMarker s_MarkerLoadContext = new ProfilerMarker("Load Context");
        private static ProfilerMarker s_MarkerSaveAsAsset = new ProfilerMarker("Save As Asset");

        public VRMAssetImportProcessor(string vrmPath, UnityPath prefabPath)
        {
            this.vrmPath = vrmPath;
            this.prefabPath = prefabPath;
            IsFinished = false;
        }

        public void Start()
        {
            sw.Start();

            s_MarkerGlbParsing.Begin();
            gltfData = new GlbFileParser(vrmPath).Parse();
            vrmData = new VRMData(gltfData);
            s_MarkerGlbParsing.End();

            pathToDescriptor = new Dictionary<string, TextureDescriptor>();

            using (var initialContext = new VRMImporterContext(vrmData))
            {
                var editor = new VRMEditorImporterContext(initialContext, prefabPath);
                // extract texture images
                // TODO: Need to handle if there are no textures
                editor.ConvertAndExtractImages(OnTexturesImportedCallback, pathToDescriptor);
            }
        }

        public void OnPreprocessTexture(TextureImporter textureImporter, string assetPath, AssetImportContext context)
        {
            s_MarkerConfigureTextures.Begin();
            if (pathToDescriptor != null && pathToDescriptor.TryGetValue(assetPath, out TextureDescriptor texDesc))
            {
                //Debug.Log($"Found Texture Descriptor [assetPath={assetPath}]");
                // Configure the texture here to try to save an extra save and load for the asset
                TextureImporterConfigurator.Configure(texDesc, textureImporter);
            }
            else
            {
                Debug.LogError($"Texture Descriptor Missing [assetPath={assetPath}]");
            }
            s_MarkerConfigureTextures.End();
        }

        /// <summary>
        /// これは EditorApplication.delayCall により呼び出される。
        /// 
        /// * delayCall には UnityEngine.Object 持ち越すことができない
        /// * vrmPath のみを持ち越す
        /// 
        /// </summary>
        /// <value></value>
        public void OnTexturesImportedCallback(IEnumerable<UnityPath> texturePaths)
        {
            s_MarkerCreatePrefab.Begin();

            Dictionary<SubAssetKey, UnityEngine.Object> map = texturePaths
                .Select(x => x.LoadAsset<Texture>())
                .ToDictionary(x => new SubAssetKey(x), x => x as UnityEngine.Object);

            var settings = new ImporterContextSettings();

            // 確実に Dispose するために敢えて再パースしている
            using (var texturesImportedContext = new VRMImporterContext(vrmData, externalObjectMap: map, settings: settings))
            {
                var editor = new VRMEditorImporterContext(texturesImportedContext, prefabPath);
                /*s_MarkerConfigureTextures.Begin();
                UnityEditorUtils.AssetEditingBlock(() =>
                {
                    foreach (var textureInfo in texturesImportedContext.TextureDescriptorGenerator.Get().GetEnumerable())
                    {
                        TextureImporterConfigurator.Configure(textureInfo, texturesImportedContext.TextureFactory.ExternalTextures);
                    }
                });
                s_MarkerConfigureTextures.End();*/

                s_MarkerLoadContext.Begin();
                var loaded = texturesImportedContext.Load();
                s_MarkerLoadContext.End();

                s_MarkerSaveAsAsset.Begin();
                UnityEditorUtils.AssetEditingBlock(() =>
                {
                    editor.SaveAsAsset(loaded);
                });
                s_MarkerSaveAsAsset.End();
            }

            s_MarkerCreatePrefab.End();

            Complete();
        }

        private void Complete()
        {
            gltfData.Dispose();

            sw.Stop();

            Debug.Log($"Import complete [importMs={sw.ElapsedMilliseconds}]");

            IsFinished = true;
        }
    }
}
