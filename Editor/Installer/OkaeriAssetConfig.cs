using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Okaeri.Editor.Installer
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

    public class OkaeriAssetConfigValidator
    {
        /// <summary>
        /// Determines if the provided Okaeri asset configuration is valid.
        /// </summary>
        /// <param name="config">The Okaeri asset configuration to check.</param>
        /// <param name="errorMessage">The validation error message.</param>
        /// <returns>True if the Okaeri asset configuration is valid.</returns>
        public static bool IsValid(OkaeriAssetConfig config, out string errorMessage)
        {
            // Initialize the errors list
            var errors = new List<string>();

            // Check the asset configuration object
            if (config != null)
            {
                // Check the fields
                var assetConfigFields = config.GetType().GetFields();
                foreach (var assetConfigField in assetConfigFields)
                {
                    // Check the field type
                    if (assetConfigField.FieldType != typeof(string))
                    {
                        continue;
                    }

                    // Check if it's a required field
                    var isRequiredAttribute = assetConfigField.GetCustomAttribute<OkaeriAssetRequiredAttribute>(true);
                    if (isRequiredAttribute == null)
                    {
                        continue;
                    }

                    // Check the field value
                    errors.Add(string.IsNullOrWhiteSpace((string)assetConfigField.GetValue(config))
                        ? $"Invalid or empty {assetConfigField.Name}"
                        : "");
                }

                // Check the paths
                if (!string.IsNullOrWhiteSpace(config.PrefabName))
                {
                    var prefabPath = Path.Combine(config.AssetPath, config.PrefabName);
                    errors.Add(File.Exists(prefabPath) ? "" : $"Prefab cannot be found at {prefabPath}");
                }

                if (!string.IsNullOrWhiteSpace(config.AssetFXAnimatorWDOff))
                {
                    var wdOffAnimatorPath = Path.Combine(config.AssetPath, config.AssetFXAnimatorWDOff);
                    errors.Add(File.Exists(wdOffAnimatorPath) ? "" : $"FX animator (WD OFF) cannot be found at {wdOffAnimatorPath}");
                }

                if (!string.IsNullOrWhiteSpace(config.AssetFXAnimatorWDOn))
                {
                    var wdOnAnimatorPath = Path.Combine(config.AssetPath, config.AssetFXAnimatorWDOn);
                    errors.Add(File.Exists(wdOnAnimatorPath) ? "" : $"FX animator (WD ON) cannot be found at {wdOnAnimatorPath}");
                }

                if (!string.IsNullOrWhiteSpace(config.AssetExpressionParams))
                {
                    var expressionParamsPath = Path.Combine(config.AssetPath, config.AssetExpressionParams);
                    errors.Add(File.Exists(expressionParamsPath) ? "" : $"Expression parameters cannot be found at {expressionParamsPath}");
                }

                if (!string.IsNullOrWhiteSpace(config.AssetExpressionsMenu))
                {
                    var expressionsMenuPath = Path.Combine(config.AssetPath, config.AssetExpressionsMenu);
                    errors.Add(File.Exists(expressionsMenuPath) ? "" : $"Expressions menu cannot be found at {expressionsMenuPath}");
                }
            }
            else
            {
                errors.Add("Invalid asset configuration: NULL");
            }

            // Return the result
            errorMessage = string.Join(Environment.NewLine, errors.Where(e => !string.IsNullOrWhiteSpace(e)));
            return string.IsNullOrWhiteSpace(errorMessage);
        }
    }
}