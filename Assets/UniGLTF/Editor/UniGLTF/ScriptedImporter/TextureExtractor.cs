using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UniGLTF
{
    internal class TextureExtractTaskData
    {
        public readonly SubAssetKey key;
        public readonly UnityPath targetPath;
        public readonly uint width;
        public readonly uint height;
        public readonly NativeArray<byte> imgBytes;
        public readonly GraphicsFormat graphicsFormat;
        public Task task;

        public TextureExtractTaskData(SubAssetKey key, UnityPath targetPath, NativeArray<byte> imgBytes, GraphicsFormat graphicsFormat, uint width, uint height)
        {
            this.key = key;
            this.targetPath = targetPath;
            this.width = width;
            this.height = height;
            this.imgBytes = imgBytes;
            this.graphicsFormat = graphicsFormat;
        }

        public void Proc()
        {
            NativeArray<byte> nativeBytes = ImageConversion.EncodeNativeArrayToPNG(imgBytes, graphicsFormat, width, height);

            // Need to copy NativeArray data to C# array for file writing
            byte[] bytes = new byte[nativeBytes.Length];
            nativeBytes.CopyTo(bytes);

            File.WriteAllBytes(targetPath.FullPath, bytes);
        }
    }

    public class TextureExtractor
    {
        const string TextureDirName = "Textures";

        GltfData m_data;
        public GltfData Data => m_data;

        public glTF GLTF => m_data.GLTF;

        public readonly Dictionary<SubAssetKey, UnityPath> Textures = new Dictionary<SubAssetKey, UnityPath>();
        private readonly IReadOnlyDictionary<SubAssetKey, Texture> m_subAssets;
        UnityPath m_textureDirectory;

        private static ProfilerMarker s_MarkerStartExtractTextures = new ProfilerMarker("Start Extract Textures");
        private static ProfilerMarker s_MarkerDelayedExtractTextures = new ProfilerMarker("Delayed Extract Textures");

        public TextureExtractor(GltfData data, UnityPath textureDirectory, IReadOnlyDictionary<SubAssetKey, Texture> subAssets)
        {
            m_data = data;
            m_textureDirectory = textureDirectory;
            m_textureDirectory.EnsureFolder();
            m_subAssets = subAssets;
        }

        public static string GetExt(string mime, string uri)
        {
            switch (mime)
            {
                case "image/png": return ".png";
                case "image/jpeg": return ".jpg";
            }

            return Path.GetExtension(uri).ToLower();
        }

        public void Extract(SubAssetKey key, TextureDescriptor texDesc)
        {
            if (Textures.ContainsKey(key))
            {
                return;
            }

            // write converted texture
            if (m_subAssets.TryGetValue(key, out var texture) && texture is Texture2D tex2D)
            {
                var targetPath = m_textureDirectory.Child($"{key.Name}.png");
                File.WriteAllBytes(targetPath.FullPath, tex2D.EncodeToPNG().ToArray());
                targetPath.ImportAsset();

                Textures.Add(key, targetPath);
            }
            else
            {
                // throw new Exception($"{key} is not converted.");
                UniGLTFLogger.Warning($"{key} is not converted.");
            }
        }

        internal bool TryAddTask(SubAssetKey key, TextureDescriptor texDesc, List<TextureExtractTaskData> tasks)
        {
            if (Textures.ContainsKey(key))
            {
                return false;
            }

            // Try to add task to convert Texture
            if (m_subAssets.TryGetValue(key, out var texture) && texture is Texture2D tex2D)
            {
                var targetPath = m_textureDirectory.Child($"{key.Name}.png");

                TextureExtractTaskData taskData = new TextureExtractTaskData(key, targetPath, tex2D.GetPixelData<byte>(0), tex2D.graphicsFormat, (uint)tex2D.width, (uint)tex2D.height);
                taskData.task = Task.Run(taskData.Proc);
                tasks.Add(taskData);

                Textures.Add(key, targetPath);

                return true;
            }
            else
            {
                UniGLTFLogger.Warning($"{key} is not converted.");
                return false;
            }
        }

        /// <summary>
        ///
        /// * Texture(.png etc...)をディスクに書き出す
        /// * EditorApplication.delayCall で処理を進めて 書き出した画像が Asset として成立するのを待つ
        /// * 書き出した Asset から TextureImporter を取得して設定する
        ///
        /// </summary>
        /// <param name="importer"></param>
        /// <param name="dirName"></param>
        /// <param name="onCompleted"></param>
        public static void ExtractTextures(GltfData data, UnityPath textureDirectory,
            ITextureDescriptorGenerator textureDescriptorGenerator, IReadOnlyDictionary<SubAssetKey, Texture> subAssets,
            Action<SubAssetKey, Texture2D> addRemap,
            Action<IEnumerable<UnityPath>> onCompleted = null)
        {
            s_MarkerStartExtractTextures.Begin();

            var extractor = new TextureExtractor(data, textureDirectory, subAssets);

            // Due to overheads of setting up threaded version low core count machines they are likely faster on the non-threaded version
            if (SystemInfo.processorCount < 4)
            {
                Extract(textureDescriptorGenerator, extractor);
            }
            else
            {
                ExtractThreaded(textureDescriptorGenerator, extractor);
            }

            s_MarkerStartExtractTextures.End();

            EditorApplication.delayCall += () =>
            {
                s_MarkerDelayedExtractTextures.Begin();

                // Wait for the texture assets to be imported

                foreach (var (key, targetPath) in extractor.Textures)
                {
                    // remap
                    var externalObject = targetPath.LoadAsset<Texture2D>();
                    if (externalObject != null)
                    {
                        addRemap(key, externalObject);
                    }
                }

                s_MarkerDelayedExtractTextures.End();

                if (onCompleted != null)
                {
                    onCompleted(extractor.Textures.Values);
                }
            };
        }

        private static void Extract(ITextureDescriptorGenerator textureDescriptorGenerator, TextureExtractor extractor)
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var param in textureDescriptorGenerator.Get().GetEnumerable())
                {
                    extractor.Extract(param.SubAssetKey, param);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        private static void ExtractThreaded(ITextureDescriptorGenerator textureDescriptorGenerator, TextureExtractor extractor)
        {
            List<TextureExtractTaskData> taskDatas = new List<TextureExtractTaskData>();
            foreach (var param in textureDescriptorGenerator.Get().GetEnumerable())
            {
                extractor.TryAddTask(param.SubAssetKey, param, taskDatas);
            }

            int count = taskDatas.Count;
            if (count > 0)
            {
                Task[] tasks = new Task[count];
                for (int i = 0; i < count; i++)
                {
                    tasks[i] = taskDatas[i].task;
                }

                // Wait for all the tasks to finish
                Task.WaitAll(tasks);

                try
                {
                    AssetDatabase.StartAssetEditing();
                    // Import asset
                    foreach (var taskData in taskDatas)
                    {
                        taskData.targetPath.ImportAsset();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }
        }
    }
}
