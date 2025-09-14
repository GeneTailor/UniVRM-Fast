using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniGLTF
{
    public struct SlimTextureExportParam
    {
        public ColorSpace ExportColorSpace { get; }

        public Func<(Texture2D, bool IsDisposable)> Creator { get; set; }

        public SlimTextureExportParam(ColorSpace exportColorSpace, Func<(Texture2D, bool IsDisposable)> creator)
        {
            ExportColorSpace = exportColorSpace;
            Creator = creator;
        }
    }

    public interface ITextureExportData
    {
        int TextureCount { get; }

        SlimTextureExportParam GetSlimTextureExportData(int index);

        void AddDisposable(Texture2D disposable);
    }

    /// <summary>
    /// Texture2D を入力として byte[] を得る機能
    /// </summary>
    public interface ITextureSerializer
    {
        /// <summary>
        /// Texture をファイルのバイト列そのまま出力してよいかどうか判断する。
        ///
        /// exportColorSpace はその Texture2D がアサインされる glTF プロパティの仕様が定める色空間を指定する。
        /// Runtime 出力では常に false が望ましい。
        /// </summary>
        bool CanExportAsEditorAssetFile(Texture texture, ColorSpace exportColorSpace);

        /// <summary>
        /// Texture2D から実際のバイト列を取得する。
        ///
        /// exportColorSpace はその Texture2D がアサインされる glTF プロパティの仕様が定める色空間を指定する。
        /// 具体的には Texture2D をコピーする際に、コピー先の Texture2D の色空間を決定するために使用する。
        /// </summary>
        (byte[] bytes, string mime) ExportBytesWithMime(Texture2D texture, ColorSpace exportColorSpace);

        /// <summary>
        /// エクスポートに使用したい Texture に対して、事前準備を行う。
        ///
        /// たとえば UnityEditor においては、Texture Asset の圧縮設定を OFF にしたりしたい。
        /// </summary>
        void ModifyTextureAssetBeforeExporting(Texture texture);

		/// <summary>
		/// Allow for batched exporting (i.e. StartAssetEditing() & StopAssetEditing() in Editor) by handing the export in the implementation.
		/// </summary>
		/// <param name="exportingList"></param>
		/// <returns></returns>
		List<(Texture2D, ColorSpace)> Export(ITextureExportData data);
	}
}
