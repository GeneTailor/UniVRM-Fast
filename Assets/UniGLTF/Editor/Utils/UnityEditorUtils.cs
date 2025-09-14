using System;
using UnityEditor;
using UnityEngine;

namespace UniGLTF.Utils
{
    public static class UnityEditorUtils
    {
        public static void AssetEditingBlock(Action assetsAction)
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                assetsAction();
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
