using System;
using UnityEngine;

namespace Okaeri.Editor
{
    public class OkaeriAssetRequiredAttribute : Attribute { }

    [CreateAssetMenu(fileName = "AssetConfig", menuName = "Okaeri/Installer/Asset Configuration")]
    public class OkaeriAssetConfig : ScriptableObject
    {
        [OkaeriAssetRequired] public string AssetName;
        [OkaeriAssetRequired] public string AssetPath;

        [OkaeriAssetRequired] public string BoothURL = "https://okaeri-shop.booth.pm";
        [OkaeriAssetRequired] public string GumroadURL = "https://gum.okaeri.moe";

        [OkaeriAssetRequired] public string PrefabName;
        [OkaeriAssetRequired] public string AssetItemName;

        public string AssetFXAnimatorWDOff;
        public string AssetFXAnimatorWDOn;

        public string AssetExpressionParams;
        public string AssetExpressionsMenu;
        public string AssetMaterialsFolder;

        public string[] MovableItems;
        public string[] ScalableItems;
    }

    [Serializable]
    public class SerializedOkaeriAssetConfig
    {
        public string name;
        public string content;
        public string crc;
    }

    [Serializable]
    public class SerializedOkaeriAssetConfigs
    {
        public SerializedOkaeriAssetConfig[] configs;
        public string error;
    }
}