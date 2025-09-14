using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UniGLTF.Utils;
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
        public readonly Dictionary<SubAssetKey, UnityPath> Textures = new Dictionary<SubAssetKey, UnityPath>();
        private readonly IReadOnlyDictionary<SubAssetKey, Texture> m_subAssets;
        UnityPath m_textureDirectory;

		private static ProfilerMarker s_MarkerCreateTextureExtractor = new ProfilerMarker("Create TextureExtractor");
		private static ProfilerMarker s_MarkerStartExtractTextures = new ProfilerMarker("Start Extract Textures");
		private static ProfilerMarker s_MarkerExtractTexturesThreaded_AddTasks = new ProfilerMarker("Threaded - Add Tasks");
		private static ProfilerMarker s_MarkerExtractTexturesThreaded_AwaitTasks = new ProfilerMarker("Threaded - Await Tasks");
		private static ProfilerMarker s_MarkerExtractTexturesThreaded_Import = new ProfilerMarker("Threaded - Import");
		private static ProfilerMarker s_MarkerAddRemapTextures = new ProfilerMarker("Add Remap Textures");

        public TextureExtractor(UnityPath textureDirectory, IReadOnlyDictionary<SubAssetKey, Texture> subAssets)
        {
            m_textureDirectory = textureDirectory;
			EnsureFolder();
			m_subAssets = subAssets;
        }

        private void EnsureFolder()
		{
            UnityEditorUtils.AssetEditingBlock(() =>
            {
                m_textureDirectory.EnsureFolder();
            });
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

        public void Extract(SubAssetKey key, TextureDescriptor texDesc, Dictionary<string, TextureDescriptor> pathToDescriptor = null)
        {
            if (Textures.ContainsKey(key))
            {
                return;
            }

            // write converted texture
            if (m_subAssets.TryGetValue(key, out var texture) && texture is Texture2D tex2D)
            {
                var targetPath = m_textureDirectory.Child($"{key.Name}.png");

                if (pathToDescriptor != null)
                    pathToDescriptor[targetPath.Value] = texDesc;

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

        internal bool TryAddTask(SubAssetKey key, TextureDescriptor texDesc, List<TextureExtractTaskData> tasks, Dictionary<string, TextureDescriptor> pathToDescriptor = null)
        {
            if (Textures.ContainsKey(key))
            {
                return false;
            }

            // Try to add task to convert Texture
            if (m_subAssets.TryGetValue(key, out var texture) && texture is Texture2D tex2D)
            {
                var targetPath = m_textureDirectory.Child($"{key.Name}.png");

                if (pathToDescriptor != null)
                    pathToDescriptor[targetPath.Value] = texDesc;

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
        public static void ExtractTextures(UnityPath textureDirectory,
            ITextureDescriptorGenerator textureDescriptorGenerator, IReadOnlyDictionary<SubAssetKey, Texture> subAssets,
            Action<SubAssetKey, Texture2D> addRemap,
            Action<IEnumerable<UnityPath>> onCompleted = null, Dictionary<string, TextureDescriptor> pathToDescriptor = null)
        {
            TextureExtractor extractor = Extract(textureDirectory, subAssets, textureDescriptorGenerator, pathToDescriptor);

            // Wait for the texture assets to be imported
            EditorApplication.delayCall += () =>
            {
                Debug.Log("Delayed call");

                if (addRemap != null)
                {
                    s_MarkerAddRemapTextures.Begin();
                    foreach (var (key, targetPath) in extractor.Textures)
                    {
                        // remap
                        var externalObject = targetPath.LoadAsset<Texture2D>();
                        if (externalObject != null)
                        {
                            addRemap(key, externalObject);
                        }
                    }
                    s_MarkerAddRemapTextures.End();
                }

                if (onCompleted != null)
                {
                    onCompleted(extractor.Textures.Values);
                }
            };
        }

		private static TextureExtractor Extract(UnityPath textureDirectory, IReadOnlyDictionary<SubAssetKey, Texture> subAssets, ITextureDescriptorGenerator textureDescriptorGenerator, Dictionary<string, TextureDescriptor> pathToDescriptor)
        {
			TextureExtractor extractor = CreateExtractor(textureDirectory, subAssets);

			s_MarkerStartExtractTextures.Begin();
			// Due to overheads of setting up threaded version low core count machines they are likely faster on the non-threaded version
			if (SystemInfo.processorCount < 4)
			{
				ExtractSequential(textureDescriptorGenerator, extractor, pathToDescriptor);
			}
			else
			{
				ExtractThreaded(textureDescriptorGenerator, extractor, pathToDescriptor);
			}
			s_MarkerStartExtractTextures.End();

			return extractor;
		}

		private static TextureExtractor CreateExtractor(UnityPath textureDirectory, IReadOnlyDictionary<SubAssetKey, Texture> subAssets)
        {
			s_MarkerCreateTextureExtractor.Begin();
			var extractor = new TextureExtractor(textureDirectory, subAssets);
			s_MarkerCreateTextureExtractor.End();
            return extractor;
		}

		private static void ExtractSequential(ITextureDescriptorGenerator textureDescriptorGenerator, TextureExtractor extractor, Dictionary<string, TextureDescriptor> pathToDescriptor)
        {
            UnityEditorUtils.AssetEditingBlock(() =>
            {
                foreach (var param in textureDescriptorGenerator.Get().GetEnumerable())
                {
                    extractor.Extract(param.SubAssetKey, param, pathToDescriptor);
                }
            });
        }

        private static void ExtractThreaded(ITextureDescriptorGenerator textureDescriptorGenerator, TextureExtractor extractor, Dictionary<string, TextureDescriptor> pathToDescriptor)
        {
            s_MarkerExtractTexturesThreaded_AddTasks.Begin();
			List<TextureExtractTaskData> taskDatas = new List<TextureExtractTaskData>();
            foreach (var param in textureDescriptorGenerator.Get().GetEnumerable())
            {
                extractor.TryAddTask(param.SubAssetKey, param, taskDatas, pathToDescriptor);
            }
			s_MarkerExtractTexturesThreaded_AddTasks.End();

			int count = taskDatas.Count;
            if (count > 0)
            {
				s_MarkerExtractTexturesThreaded_AwaitTasks.Begin();
				Task[] tasks = new Task[count];
                for (int i = 0; i < count; i++)
                {
                    tasks[i] = taskDatas[i].task;
                }

                // Wait for all the tasks to finish
                Task.WaitAll(tasks);
                s_MarkerExtractTexturesThreaded_AwaitTasks.End();

				s_MarkerExtractTexturesThreaded_Import.Begin();
                UnityEditorUtils.AssetEditingBlock(() =>
                {
                    // Import asset
                    foreach (var taskData in taskDatas)
                    {
                        taskData.targetPath.ImportAsset();
                    }
                });
                s_MarkerExtractTexturesThreaded_Import.End();
			}
        }
    }
}
