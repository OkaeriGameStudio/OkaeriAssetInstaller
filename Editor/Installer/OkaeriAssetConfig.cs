using System;
using UnityEngine;

namespace Okaeri.Editor
{
    [CreateAssetMenu(fileName = "AssetConfig", menuName = "Okaeri/Installer/Asset Configuration")]
    public class OkaeriAssetConfig : ScriptableObject
    {
        public string AssetName;
        public string AssetPath;

        public string BoothURL = "https://okaeri-shop.booth.pm";
        public string GumroadURL = "https://gum.okaeri.moe";

        public string PrefabName;
        public string AssetItemName;

        public string AssetFXAnimatorWDOff;
        public string AssetFXAnimatorWDOn;

        public string AssetExpressionParams;
        public string AssetExpressionsMenu;

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