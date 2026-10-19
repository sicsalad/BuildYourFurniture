#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BuildYourFurniture.Editor
{
    public sealed class UiSpriteImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/UI/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
        }

        [MenuItem("BuildYourFurniture/Refresh UI Images")]
        static void Refresh()
        {
            AssetDatabase.ImportAsset("Assets/Resources/UI", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("BuildYourFurniture UI images refreshed.");
        }
    }
}
#endif
