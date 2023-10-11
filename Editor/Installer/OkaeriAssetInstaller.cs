//VERSION1.0.9
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRLabs.AV3Manager;

namespace Okaeri.Editor
{
    public class OkaeriAssetInstaller : EditorWindow
    {
        /// <summary>
        /// Unity project relative path to the Okaeri asset installer path.
        /// </summary>
        private static string INSTALLER_PATH;

        /// <summary>
        /// Unity project relative path to the Okaeri asset installer resources.
        /// </summary>
        private static string INSTALLER_RESOURCES_PATH => INSTALLER_PATH + "\\Resources";

        /// <summary>
        /// Unity project relative path to the Okaeri asset installer configurations.
        /// </summary>
        private static string[] INSTALLER_CONFIGS_PATH => new[] { INSTALLER_PATH + "\\Configs" };

        /// <summary>
        /// Unity project relative path to the Okaeri asset installer temporary folder.
        /// </summary>
        private static string INSTALLER_TEMP_FOLDER => INSTALLER_PATH + "\\tmp";

        /// <summary>
        /// String representation of an empty asset configuration list.
        /// </summary>
        private static string INSTALLER_CONFIGS_EMPTY_JSON = "{\"configs\":[]}";

        /// <summary>
        /// The asset configuration search filter.
        /// </summary>
        private const string INSTALLER_CONFIGS_FILTER = "t:OkaeriAssetConfig";

        /// <summary>
        /// Recursively creates the provided folder path.
        /// </summary>
        /// <param name="folder">The folder path.</param>
        private static void CreateFolder(string folder)
        {
            // Get the subfolders
            var subfolders = folder.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (subfolders.Length == 0)
            {
                throw new ArgumentException("Could not create the folder path(s): Invalid folder provided.");
            }

            // Go through the subfolders
            var lastCheckedFolder = subfolders[0].Equals("Assets") ? subfolders[0] : string.Empty;
            foreach (var subfolder in subfolders)
            {
                if (subfolder.Equals(lastCheckedFolder))
                {
                    continue;
                }

                var currentSubfolder = lastCheckedFolder + "\\" + subfolder;
                if (!AssetDatabase.IsValidFolder(currentSubfolder))
                {
                    AssetDatabase.CreateFolder(lastCheckedFolder, subfolder);
                    AssetDatabase.Refresh();
                }

                lastCheckedFolder = currentSubfolder;
            }
        }

        /// <summary>
        /// Unpacks the Prefab at the specified path.
        /// </summary>
        /// <param name="prefabPath">The path to the Prefab to unpack.</param>
        /// <returns>The unpacked prefab object.</returns>
        private static GameObject UnpackPrefab(string prefabPath)
        {
            // Check the given path
            if (!File.Exists(prefabPath))
            {
                throw new FileNotFoundException("Cannot unpack the prefab: Empty or invalid prefab path provided.");
            }

            // Attempt to load the prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new InvalidDataException("Cannot load the prefab: Empty or invalid prefab.");
            }

            // Instantiate the prefab
            var result = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (result == null)
            {
                throw new InvalidDataException("Cannot instantiate prefab: Empty or invalid prefab.");
            }

            // Unpack the prefab
            result.transform.SetAsLastSibling();
            PrefabUtility.UnpackPrefabInstance(result, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return result;
        }

        #region Banner

        /// <summary>
        /// The installer window banner image.
        /// </summary>
        private Texture2D m_bannerImage;

        /// <summary>
        /// Draws the installer banner.
        /// </summary>
        private void DrawBanner()
        {
            if (m_bannerImage == null)
            {
                m_bannerImage = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.Combine(INSTALLER_RESOURCES_PATH, "banner.png"));
            }

            var guiStyle = GUI.skin.GetStyle("Label");
            guiStyle.alignment = TextAnchor.UpperCenter;
            GUILayout.Label(m_bannerImage, guiStyle, GUILayout.ExpandWidth(true), GUILayout.Height(100));

            GUILayout.Space(16);
        }

        #endregion

        #region Asset Configurations

        /// <summary>
        /// A dictionary of available asset configurations, where the key is their path.
        /// </summary>
        private Dictionary<string, OkaeriAssetConfig> m_localConfigs;

        /// <summary>
        /// The HTTPClient instance to use for requests.
        /// </summary>
        private readonly HttpClient m_httpClient = new HttpClient();

        /// <summary>
        /// Returns a list of Okaeri Asset Installer Configuration paths.
        /// </summary>
        /// <returns>A list of Okaeri Asset Installer Configuration paths.</returns>
        private IEnumerable<string> GetLocalAssetConfigurations()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return AssetDatabase
                .FindAssets(INSTALLER_CONFIGS_FILTER, INSTALLER_CONFIGS_PATH)
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
        }

        /// <summary>
        /// Retrieves the latest version for the specified Okaeri asset configurations.
        /// </summary>
        /// <param name="configs">An array of asset configurations to update. If null, all asset configurations will get updated.</param>
        private async Task<SerializedOkaeriAssetConfigs> GetLatestAssetConfigurations()
        {
            const string errorTitle = "Asset configuration update error";
            var warningPrefix = $"Warning|{errorTitle}:\n";
            var errorPrefix = $"Error|{errorTitle}:\n";
            var result = new SerializedOkaeriAssetConfigs();

            try
            {
                // GET the latest configuration details
                var latestConfigsResponse = await m_httpClient.GetAsync(
                    "https://rammyvps.vps.webdock.cloud/api/configs", HttpCompletionOption.ResponseContentRead);

                // Read and deserialize and set the latest configuration details
                var latestConfigsJSON = await latestConfigsResponse.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(latestConfigsJSON) || latestConfigsJSON.Equals(INSTALLER_CONFIGS_EMPTY_JSON))
                {
                    result.error = warningPrefix + "Empty asset configuration information.";
                    return result;
                }

                // Deserialize the configuration details
                EditorJsonUtility.FromJsonOverwrite(latestConfigsJSON, result);
            }
            catch (ArgumentException e)
            {
                if (e.Message.Equals("JSON parse error: Invalid value."))
                {
                    result.error = errorPrefix + "Could not deserialize the asset configuration information.";
                    return result;
                }

                result.error = errorPrefix + e.Message;
            }
            catch (Exception e)
            {
                // Set the error message
                result.error = errorPrefix + e.Message;
            }

            return result;
        }

        /// <summary>
        /// Retrieves the local asset configurations.
        /// </summary>
        /// <returns>A dictionary where the keys are the name of the asset configuration and the value is the md5 checksum.</returns>
        private Dictionary<string, string> GetLocalAssetConfigurationsCRCs()
        {
            // Initialize the result
            var result = new Dictionary<string, string>();

            // Load the configurations in the folder
            foreach (var configPath in GetLocalAssetConfigurations())
            {
                // Get the OS path
                var osPath = Application.dataPath + configPath.Replace("Assets", "");
                var configCRC = string.Empty;

                // Get the file md5 checksum
                using (var md5 = MD5.Create())
                {
                    var md5Bytes = md5.ComputeHash(File.ReadAllBytes(osPath));
                    configCRC = BitConverter.ToString(md5Bytes).Replace("-", "").ToLower();
                }

                // Create the entry
                result.Add(configPath, configCRC);
            }

            // Return the asset configurations CRCs
            return result;
        }

        /// <summary>
        /// Loads the asset configurations.
        /// </summary>
        private void LoadAssetConfigurations()
        {
            // Initialize the configurations
            if (m_localConfigs == null)
            {
                m_localConfigs = new Dictionary<string, OkaeriAssetConfig>();
            }

            // Load the asset configurations
            m_localConfigs.Clear();
            foreach (var configPath in GetLocalAssetConfigurations())
            {
                m_localConfigs.Add(configPath, AssetDatabase.LoadAssetAtPath<OkaeriAssetConfig>(configPath));
            }
        }

        /// <summary>
        /// Checks for asset configuration updates.
        /// </summary>
        private async void UpdateAssetConfigurations()
        {
            // Try to update the configs
            try
            {
                // Check if the configurations folder is valid
                var configsPath = INSTALLER_CONFIGS_PATH[0];
                if (!AssetDatabase.IsValidFolder(configsPath))
                {
                    CreateFolder(configsPath);
                }

                // Get the latest asset configurations
                var latestConfigs = await GetLatestAssetConfigurations();
                if (!string.IsNullOrWhiteSpace(latestConfigs.error))
                {
                    // Log error message
                    var errorMessage = latestConfigs.error.Split('|')[1];
                    UnityEngine.Debug.LogWarning("[Okaeri] " + errorMessage);

                    // Load configurations
                    LoadAssetConfigurations();
                    return;
                }

                // Get the local asset configurations
                var localConfigsCRCs = GetLocalAssetConfigurationsCRCs();

                // Create or update asset configurations
                foreach (var config in latestConfigs.configs)
                {
                    var configPath = Path.Combine(configsPath, config.name);
                    if (!File.Exists(configPath) || !localConfigsCRCs.ContainsKey(configPath))
                    {
                        File.WriteAllText(configPath, config.content, Encoding.UTF8);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                        continue;
                    }

                    var localCrc = localConfigsCRCs[configPath];
                    if (localCrc.Equals(config.crc))
                    {
                        continue;
                    }

                    AssetDatabase.DeleteAsset(configPath);
                    File.WriteAllText(configPath, config.content);
                }
            }
            catch
            {
                // Logs if the server is dead or no internet
                UnityEngine.Debug.Log("<color=pink>[Okaeri]" + "Couldn't connect to the server! Proceeding without configs updates...");
            }
            finally
            {
                // Load the configurations
                LoadAssetConfigurations();
            }
        }

        #endregion

        #region Installer Window

        /// <summary>
        /// Determines if we can install stuff.
        /// </summary>
        private bool m_canInstall;

        /// <summary>
        /// Determines if the installer is installing an asset.
        /// </summary>
        private bool m_installing;

        /// <summary>
        /// Determines if the installer is positioning the asset.
        /// </summary>
        private bool m_moveRotateScale;

        /// <summary>
        /// The asset installation log.
        /// </summary>
        private readonly List<string> m_installLog = new List<string>();

        /// <summary>
        /// The asset installation log scroll.
        /// </summary>
        private Vector2 m_installLogScroll = Vector2.zero;

        /// <summary>
        /// The list of available Okaeri assets.
        /// </summary>
        private string[] m_assetNames;

        /// <summary>
        /// The selected asset configuration.
        /// </summary>
        private OkaeriAssetConfig m_selectedAssetConfig;

        /// <summary>
        /// The avatar to install the asset on.
        /// </summary>
        private VRCAvatarDescriptor m_avatar;

        /// <summary>
        /// The animator from the selected avatar.
        /// </summary>
        private Animator m_animator;

        /// <summary>
        /// Determines if the selected avatar is valid.
        /// </summary>
        private bool m_validAvatar;

        /// <summary>
        /// The Discord icon to use in the footer.
        /// </summary>
        private Texture m_discordIcon;

        /// <summary>
        /// Handles the installer window initialization.
        /// </summary>
        private void OnEnable()
        {
            // Get current path
            var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            INSTALLER_PATH = Path.GetDirectoryName(scriptPath);

            // Updates on the configurations
            UpdateAssetConfigurations();
        }

        /// <summary>
        /// Main installer drawing function.
        /// </summary>
        private void OnGUI()
        {
            // Header
            DrawBanner();

            // Avatar selection
            DrawAvatarField();
            if (!m_validAvatar)
            {
                GUILayout.FlexibleSpace();
                DrawFooter();
                return;
            }

            // Asset configuration selection
            DrawAssetConfigurationSelection();

            // Check what controls we show
            if (m_canInstall)
            {
                if (m_moveRotateScale)
                {
                    DrawAssetMoveRotateScale();
                }
                else
                {
                    DrawAssetInstallOptions();
                    DrawAssetInstallLog();
                }
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            // Footer
            DrawFooter();
        }

        /// <summary>
        /// Draws the avatar selection field.
        /// </summary>
        private void DrawAvatarField()
        {
            // Draw the field and check for changes
            EditorGUI.BeginChangeCheck();
            m_avatar = EditorGUILayout.ObjectField("Avatar:", m_avatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;

            // Check if the avatar has been set
            if (m_avatar == null)
            {
                m_validAvatar = false;
                EditorGUILayout.HelpBox("Please select the VRC Avatar to install the assets on.", MessageType.Info);
                return;
            }

            // Check for changes to the avatar
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            // Check if the avatar has an animator
            m_animator = m_avatar.gameObject.GetComponent<Animator>();
            if (m_animator == null)
            {
                m_validAvatar = false;
                EditorGUILayout.HelpBox("No Animator component found on the selected avatar.", MessageType.Error);
                return;
            }

            // Check if the animator is humanoid
            if (!m_animator.isHuman)
            {
                m_validAvatar = false;
                EditorGUILayout.HelpBox("Selected avatar doesn't have a humanoid rig.", MessageType.Error);
                return;
            }

            // Set the flag
            m_validAvatar = true;
        }

        /// <summary>
        /// Draws the Okaeri asset configuration selection dropdown.
        /// </summary>
        private void DrawAssetConfigDropdown()
        {
            // Initialize the list of asset names
            m_assetNames = m_localConfigs.Values.Select(c => c.AssetName).ToArray();

            // Select the asset configuration
            var selectedAssetIndex = m_selectedAssetConfig == null
                ? 0
                : Array.IndexOf(m_assetNames, m_selectedAssetConfig.AssetName);
            selectedAssetIndex = EditorGUILayout.Popup("Asset to install:", selectedAssetIndex, m_assetNames);

            var selectedAssetName = m_assetNames[selectedAssetIndex];
            m_selectedAssetConfig = m_localConfigs.Values.First(c => c.AssetName.Equals(selectedAssetName));
        }

        /// <summary>
        /// Draws the dropdown selection for the available Okaeri Asset configurations.
        /// </summary>
        private void DrawAssetConfigurationSelection()
        {
            // Check if we have any configurations to draw.
            if (m_localConfigs == null || m_localConfigs.Count == 0)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawAssetConfigDropdown();
            if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh", "Refresh asset configurations"), GUILayout.Width(32), GUILayout.Height(18)))
            {
                LoadAssetConfigurations();
            }
            EditorGUILayout.EndHorizontal();

            // Verify if we have the asset package installed
            if (!AssetDatabase.IsValidFolder(m_selectedAssetConfig.AssetPath))
            {
                EditorGUILayout.HelpBox($"The selected asset is not in the current project.\nPlease make sure you install the {m_selectedAssetConfig.AssetName} asset before continuing!", MessageType.Error);
                EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Get it on BOOTH"))
                {
                    Process.Start(m_selectedAssetConfig.BoothURL);
                }
                if (GUILayout.Button("Get it on Gumroad"))
                {
                    Process.Start(m_selectedAssetConfig.GumroadURL);
                }

                EditorGUILayout.EndHorizontal();
                m_canInstall = false;
                return;
            }

            // Show the asset package location
            EditorGUILayout.HelpBox($"{m_selectedAssetConfig.AssetName} asset found at {m_selectedAssetConfig.AssetPath}", MessageType.None);
            GUILayout.Space(8);
            m_canInstall = true;
        }

        /// <summary>
        /// Draws the asset install options.
        /// </summary>
        private void DrawAssetInstallOptions()
        {
            var titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField("Install Options:", titleStyle);
            GUILayout.Space(8);

            // Add install options
            GUI.enabled = false;
            m_installItems = EditorGUILayout.ToggleLeft("Install Asset Items", m_installItems, GUILayout.ExpandWidth(true));
            m_installAnimator = EditorGUILayout.ToggleLeft("Install Animator", m_installAnimator, GUILayout.ExpandWidth(true));
            if (m_installAnimator)
            {
                var guiEnabled = GUI.enabled;
                GUI.enabled = m_installAnimator;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);
                m_fxAnimatorWD = EditorGUILayout.ToggleLeft("Write Defaults ON", m_fxAnimatorWD,
                    GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
                GUI.enabled = guiEnabled;
            }
            m_installParameters = EditorGUILayout.ToggleLeft("Install VRC Parameters", m_installParameters, GUILayout.ExpandWidth(true));
            GUI.enabled = !m_installing;
            m_installMenu = EditorGUILayout.ToggleLeft("Install VRC Menu", m_installMenu, GUILayout.ExpandWidth(true));

            // Draw the install button
            if (GUILayout.Button($"Install {m_selectedAssetConfig.AssetName}", GUILayout.Height(32), GUILayout.ExpandWidth(true)))
            {
                // Checks
                m_installLog.Clear();
                PreInstalChecks(m_selectedAssetConfig);

                // Install
                m_installing = true;
                InstallAsset(m_selectedAssetConfig);
            }

            // Draw the move / rotate button
            if (GUILayout.Button($"Reposition {m_selectedAssetConfig.AssetName}'s Items", GUILayout.Height(32), GUILayout.ExpandWidth(true)))
            {
                m_moveRotateScale = true;
                DrawAssetMoveRotateScale();
            }

            // Draw the uninstall button
            if (GUILayout.Button($"Uninstall {m_selectedAssetConfig.AssetName}", GUILayout.Height(32), GUILayout.ExpandWidth(true)))
            {
                m_installLog.Clear();
                UninstallAsset(m_selectedAssetConfig);
            }
            GUI.enabled = true;
        }

        /// <summary>
        /// Draws the asset install log.
        /// </summary>
        private void DrawAssetInstallLog()
        {
            // Get the log text
            const string logPrefix = ">  ";
            var logText = new StringBuilder();
            foreach (var logLine in m_installLog)
            {
                // Append the console symbol
                logText.Append(logPrefix);

                // Parse the line
                var lineParts = logLine.Split('|');
                var isError = lineParts[0].Equals("e");
                var lineColor = isError ? "red" : "grey";
                var text = lineParts[1];
                var lineText = isError ? $"<b>{text}</b>" : text;

                // Append the line
                logText.Append($"<color={lineColor}>{lineText}</color>");
                logText.Append(Environment.NewLine);
            }

            // Draw the log
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 12,
                richText = true,
                wordWrap = true
            };

            m_installLogScroll = EditorGUILayout.BeginScrollView(m_installLogScroll);
            GUI.enabled = false;
            EditorGUILayout.TextArea(logText.ToString(), style, GUILayout.ExpandHeight(true));
            GUI.enabled = true;
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Draws the Okaeri asset installer footer.
        /// </summary>
        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(24));
            GUILayout.FlexibleSpace();

            var labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                stretchWidth = false,
                contentOffset = new Vector2(0, 3)
            };
            GUILayout.Label("Need help or have questions?", labelStyle, GUILayout.Width(170));

            if (m_discordIcon == null)
            {
                m_discordIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.Combine(INSTALLER_RESOURCES_PATH, "discord.png"));
            }
            GUILayout.Label(m_discordIcon, GUILayout.Width(24), GUILayout.Height(24));

            var linkStyle = new GUIStyle(EditorStyles.linkLabel)
            {
                fontStyle = FontStyle.Bold
            };
            if (GUILayout.Button("Join us on Discord!", linkStyle))
            {
                Process.Start("https://discord.okaeri.moe");
            }
            GUILayout.Space(8);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Installer Checks

        /// <summary>
        /// Perform some sanity checks before installation.
        /// </summary>
        private void PreInstalChecks(OkaeriAssetConfig assetConfig)
        {
            string errorMessage;
            m_installLog.Add("i|Performing pre-install checks");

            // Check if the given configuration is valid
            m_installLog.Add("i|\tValidating asset configuration");
            if (assetConfig == null)
            {
                errorMessage = "Cannot install the specified Okaeri asset configuration: Invalid or empty asset configuration.";
                m_installLog.Add($"e|{errorMessage}");
                throw new ArgumentNullException(errorMessage);
            }

            // Check if the asset is already installed on the avatar
            m_installLog.Add("i|\tChecking if asset items are already installed on the avatar");
            var assetItemsOnAvatar = GetAssetItemsOnAvatar(assetConfig);
            if (assetItemsOnAvatar.Length > 0)
            {
                errorMessage = $"Cannot install {assetConfig.AssetName}: Asset items are already installed.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Check if the animator already has some of the asset layers
            m_installLog.Add("i|\tChecking avatar animator layers and parameters");
            var assetAnimLayersAndParametersOnAvatar = GetAssetAnimatorLayersAndParametersOnAvatar(assetConfig);
            if (assetAnimLayersAndParametersOnAvatar.Item1.Length > 0)
            {
                errorMessage = $"Cannot install {assetConfig.AssetName}: Avatar FX animator already contains some layers from the asset.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }
            if (assetAnimLayersAndParametersOnAvatar.Item2.Length > 0)
            {
                errorMessage = $"Cannot install {assetConfig.AssetName}: Avatar FX animator already contains some parameters from the asset.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Check if the parameters can be installed on the avatar
            m_installLog.Add("i|\tChecking avatar expression parameters");
            CheckExpressionParametersCost(assetConfig);

            // Check if the menu can be installed on the avatar
            m_installLog.Add("i|\tChecking avatar expressions menu");
            CheckExpressionsMenuSpace(assetConfig);
        }

        /// <summary>
        /// Returns a list of the specified Okaeri asset items present on the current avatar.
        /// </summary>
        /// <param name="assetConfig">The asset configuration.</param>
        /// <returns></returns>
        private Transform[] GetAssetItemsOnAvatar(OkaeriAssetConfig assetConfig)
        {
            // Get the avatar items
            var avatarItems = m_avatar.transform.GetComponentsInChildren<Transform>(true);
            var avatarItemsNames = avatarItems.Select(t => t.name).ToArray();

            // Get the asset items list
            var assetPrefab = UnpackPrefab(Path.Combine(assetConfig.AssetPath, assetConfig.PrefabName));
            var assetPrefabItems = new List<Transform>();
            for (var i = 0; i < assetPrefab.transform.childCount; ++i)
            {
                var child = assetPrefab.transform.GetChild(i);
                if (child.name.Equals("Items"))
                {
                    assetPrefabItems.Add(child.GetChild(0));
                    continue;
                }

                assetPrefabItems.Add(child);
            }

            // Get the asset items names that are also on the avatar
            var assetItemsNamesOnAvatar = avatarItemsNames.Intersect(assetPrefabItems.Select(t => t.name)).ToArray();

            // Return the installed asset items list
            DestroyImmediate(assetPrefab);
            return avatarItems.Where(t => assetItemsNamesOnAvatar.Contains(t.name)).ToArray();
        }

        /// <summary>
        /// Gets the asset item on avatar.
        /// </summary>
        /// <returns></returns>
        private Transform GetAssetItemOnAvatar(OkaeriAssetConfig assetConfig)
        {
            var avatarItems = m_avatar.transform.Find("Items");
            if (avatarItems == null)
            {
                throw new InstanceNotFoundException("Did not find the Items object on the current avatar.");
            }
            return avatarItems.Find(assetConfig.AssetItemName);
        }

        /// <summary>
        /// Returns the index for the current avatar FX animation layer.
        /// </summary>
        /// <returns></returns>
        private int GetAvatarFxAnimationLayerIndex()
        {
            var result = -1;
            for (var i = 0; i < m_avatar.baseAnimationLayers.Length; ++i)
            {
                if (m_avatar.baseAnimationLayers[i].type.Equals(VRCAvatarDescriptor.AnimLayerType.FX))
                {
                    result = i;
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// Returns a Tuple of lists with the specified Okaeri asset animator layers and animator parameters present on the current avatar.
        /// </summary>
        /// <param name="assetConfig">The asset configuration.</param>
        /// <returns></returns>
        private Tuple<string[], string[]> GetAssetAnimatorLayersAndParametersOnAvatar(OkaeriAssetConfig assetConfig)
        {
            // Get the avatar FX animator layers
            var avatarFxAnimLayerIndex = GetAvatarFxAnimationLayerIndex();
            if (avatarFxAnimLayerIndex == -1)
            {
                return null;
            }

            var avatarFxAnimController = m_avatar.baseAnimationLayers[avatarFxAnimLayerIndex].animatorController as AnimatorController;
            if (avatarFxAnimController == null)
            {
                // Use blank FX controller
                var blankFXControllerPath = $"Assets/{m_avatar.name}_FX.controller";
                AssetDatabase.CopyAsset(Path.Combine(INSTALLER_RESOURCES_PATH, "BlankFX.controller"), blankFXControllerPath);
                AssetDatabase.Refresh();
                m_avatar.baseAnimationLayers[avatarFxAnimLayerIndex].animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(blankFXControllerPath);
                avatarFxAnimController = m_avatar.baseAnimationLayers[avatarFxAnimLayerIndex].animatorController as AnimatorController;
            }

            var avatarFxAnimLayersNames = avatarFxAnimController.layers.Select(l => l.name);
            var avatarFxAnimParametersNames = avatarFxAnimController.parameters.Select(l => l.name);

            // Get the asset FX animator layers
            var wdOffAssetFxAnimController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(Path.Combine(assetConfig.AssetPath,
                    assetConfig.AssetFXAnimatorWDOff));
            var wdOffAssetFxAnimLayersNames = wdOffAssetFxAnimController.layers.Select(l => l.name);
            var wdOffAssetFxAnimParametersNames = wdOffAssetFxAnimController.parameters.Select(l => l.name);

            var wdOnAssetFxAnimController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(Path.Combine(assetConfig.AssetPath,
                    assetConfig.AssetFXAnimatorWDOn));
            var wdOnAssetFxAnimLayersNames = wdOnAssetFxAnimController.layers.Select(l => l.name);
            var wdOnAssetFxAnimParametersNames = wdOnAssetFxAnimController.parameters.Select(l => l.name);

            // Get the layer names
            var wdOffLayers = avatarFxAnimLayersNames.Intersect(wdOffAssetFxAnimLayersNames);
            var wdOnLayers = avatarFxAnimLayersNames.Intersect(wdOnAssetFxAnimLayersNames);
            var assetAnimLayersOnAvatar = wdOffLayers.Concat(wdOnLayers).ToArray();

            // Get the parameter names
            var wdOffParameters = avatarFxAnimParametersNames.Intersect(wdOffAssetFxAnimParametersNames);
            var wdOnParameters = avatarFxAnimParametersNames.Intersect(wdOnAssetFxAnimParametersNames);
            var assetAnimParametersOnAvatar = wdOffParameters.Concat(wdOnParameters).Distinct().Except(AV3Manager.VrcParameters).ToArray();

            // Return the result
            return new Tuple<string[], string[]>(assetAnimLayersOnAvatar, assetAnimParametersOnAvatar);
        }

        /// <summary>
        /// Returns a list of with the specified Okaeri asset VRCExpressionParameters names present on the current avatar.
        /// </summary>
        /// <param name="assetConfig">The asset configuration.</param>
        /// <returns></returns>
        private string[] GetAssetExpressionParametersOnAvatar(OkaeriAssetConfig assetConfig)
        {
            // Get the avatar expression parameters
            var avatarExpressionParameters = m_avatar.expressionParameters.parameters.Select(p => p.name);

            // Get the asset expression parameters
            var assetExpressionParametersPath = Path.Combine(assetConfig.AssetPath, assetConfig.AssetExpressionParams);
            var assetExpressionParametersObject =
                AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(assetExpressionParametersPath);
            var assetExpressionParameters = assetExpressionParametersObject.parameters.Select(p => p.name);

            // Return the result
            return avatarExpressionParameters.Intersect(assetExpressionParameters).ToArray();
        }

        /// <summary>
        /// Checks if the asset expression parameters fit onto the avatar.
        /// </summary>
        /// <param name="assetConfig">The asset configuration.</param>
        /// <exception cref="InvalidOperationException"></exception>
        private void CheckExpressionParametersCost(OkaeriAssetConfig assetConfig)
        {
            string errorMessage;

            // Get the avatar expression parameters
            var avatarExpressionParameters = m_avatar.expressionParameters;
            if (avatarExpressionParameters == null)
            {
                // Use blank expression parameters
                var blankParametersPath = $"Assets/{m_avatar.name}_Parameters.asset";
                AssetDatabase.CopyAsset(Path.Combine(INSTALLER_RESOURCES_PATH, "BlankParameters.asset"), blankParametersPath);
                AssetDatabase.Refresh();
                m_avatar.expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(blankParametersPath);
                avatarExpressionParameters = m_avatar.expressionParameters;
            }

            // Get the asset expression parameters
            var assetExpressionParametersPath =
                m_selectedAssetConfig.AssetPath + "/" + m_selectedAssetConfig.AssetExpressionParams;
            var assetExpressionParameters =
                AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(assetExpressionParametersPath);
            if (assetExpressionParameters == null)
            {
                errorMessage = "Asset has no expression parameters object.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Check if we have space for the asset parameters
            var avatarExpressionParametersCost = avatarExpressionParameters.CalcTotalCost();
            var assetExpressionParametersCost = assetExpressionParameters.CalcTotalCost();
            if (avatarExpressionParametersCost + assetExpressionParametersCost >
                VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                errorMessage = $"Error merging the expression parameters: Not enough space for the asset parameters.\n\nThe asset requires {assetExpressionParametersCost} parameters.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Set the expression parameters to merge
            m_avatarExpressionParameters = m_avatar.expressionParameters;
            m_assetExpressionParameters = assetExpressionParameters;
        }

        /// <summary>
        /// Checks if the asset expressions menu can be installed on the avatar.
        /// </summary>
        /// <param name="assetConfig">The asset configuration.</param>
        /// <exception cref="InvalidOperationException"></exception>
        private void CheckExpressionsMenuSpace(OkaeriAssetConfig assetConfig)
        {
            string errorMessage;

            // Get the avatar expressions menu
            var avatarExpressionsMenu = m_avatar.expressionsMenu;
            if (avatarExpressionsMenu == null)
            {
                // Use blank expressions menu
                var blankMenuPath = $"Assets/{m_avatar.name}_Menu.asset";
                AssetDatabase.CopyAsset(Path.Combine(INSTALLER_RESOURCES_PATH, "BlankMenu.asset"), blankMenuPath);
                AssetDatabase.Refresh();
                m_avatar.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(blankMenuPath);
                avatarExpressionsMenu = m_avatar.expressionsMenu;
            }

            // Get the asset expressions menu
            var assetExpressionsMenuPath =
                m_selectedAssetConfig.AssetPath + "/" + m_selectedAssetConfig.AssetExpressionsMenu;
            var assetExpressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(assetExpressionsMenuPath);
            if (assetExpressionsMenu == null)
            {
                errorMessage = "Asset has no expressions menu.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Check if the menu already exists
            if (avatarExpressionsMenu.controls.Any(c => IsAssetSubmenu(c, assetExpressionsMenu)))
            {
                m_installLog.Add("i|\tAsset expressions menu already installed!");
                return;
            }

            // Check if we can add the expressions menu
            if (avatarExpressionsMenu.controls.Count == VRCExpressionsMenu.MAX_CONTROLS)
            {
                errorMessage = "No space for additional menu controls available.";
                m_installLog.Add($"e|{errorMessage}");
                EditorUtility.DisplayDialog("Error", errorMessage, "Ok");
                throw new InvalidOperationException(errorMessage);
            }

            // Set the expressions menus
            m_avatarExpressionsMenu = m_avatar.expressionsMenu;
            m_assetExpressionsMenu = assetExpressionsMenu;
        }

        /// <summary>
        /// Determines if the given VRCExpressionsMenu control is from the specified asset.
        /// </summary>
        /// <param name="control">The VRCExpressionMenu control to check.</param>
        /// <param name="assetMenu">The asset expressions menu.</param>
        /// <returns>True if the VRCExpressionsMenu control is from the asset.</returns>
        private bool IsAssetSubmenu(VRCExpressionsMenu.Control control, VRCExpressionsMenu assetMenu)
        {
            return control != null &&
                   control.type.Equals(VRCExpressionsMenu.Control.ControlType.SubMenu) &&
                   control.subMenu != null &&
                   control.subMenu.name.Equals(assetMenu.name);
        }

        #endregion

        #region Installer Logic

        /// <summary>
        /// Determines if the installer adds the asset items.
        /// </summary>
        private bool m_installItems = true;

        /// <summary>
        /// Determines if the installer automatically merges the asset animations.
        /// </summary>
        private bool m_installAnimator = true;

        /// <summary>
        /// Determines if the FX animator controller has write defaults.
        /// </summary>
        private bool m_fxAnimatorWD = false;

        /// <summary>
        /// Determines if the installer automatically merges the asset expression parameters.
        /// </summary>
        private bool m_installParameters = true;

        /// <summary>
        /// Determines if the installer automatically merges the asset expressions menu.
        /// </summary>
        private bool m_installMenu = true;

        /// <summary>
        /// A dictionary containing the names of the asset items and the armature bones they should be assigned to.
        /// </summary>
        private readonly Dictionary<string, HumanBodyBones> m_itemBones = new Dictionary<string, HumanBodyBones>
        {
            { "Head", HumanBodyBones.Head },
            { "Neck", HumanBodyBones.Neck },
            { "Chest", HumanBodyBones.Chest },
            { "Back", HumanBodyBones.Chest },
            { "HandL", HumanBodyBones.LeftHand },
            { "HandR", HumanBodyBones.RightHand },
            { "LowerLegL", HumanBodyBones.LeftLowerLeg },
            { "LowerLegR", HumanBodyBones.RightLowerLeg }
        };

        /// <summary>
        /// The prefab being installed.
        /// </summary>
        private GameObject m_assetPrefab;

        /// <summary>
        /// The main asset item.
        /// </summary>
        private Transform m_assetItem;

        /// <summary>
        /// The expression parameters from the selected avatar.
        /// </summary>
        private VRCExpressionParameters m_avatarExpressionParameters;

        /// <summary>
        /// The expression parameters from the selected asset.
        /// </summary>
        private VRCExpressionParameters m_assetExpressionParameters;

        /// <summary>
        /// The expressions menu from the selected avatar.
        /// </summary>
        private VRCExpressionsMenu m_avatarExpressionsMenu;

        /// <summary>
        /// The expressions menu from the selected asset.
        /// </summary>
        private VRCExpressionsMenu m_assetExpressionsMenu;

        /// <summary>
        /// Installs the specified Okaeri asset on the currently selected avatar.
        /// </summary>
        /// <param name="assetConfig">The Okaeri asset configuration to use for installing.</param>
        private void InstallAsset(OkaeriAssetConfig assetConfig)
        {
            var success = false;

            try
            {
                // Unpack the prefab
                m_installLog.Add("i|Unpacking asset prefab");
                var assetPrefabPath = Path.Combine(assetConfig.AssetPath, assetConfig.PrefabName);
                m_assetPrefab = UnpackPrefab(assetPrefabPath);

                // Get the prefab items
                if (!m_assetPrefab.TryGetComponent<Transform>(out var assetRoot))
                {
                    var errorMessage = "Cannot install the specified Okaeri asset: Empty or invalid prefab.";
                    m_installLog.Add($"e|{errorMessage}");
                    throw new InvalidDataException(errorMessage);
                }
                var assetItems = new Transform[assetRoot.childCount];
                for (var i = 0; i < assetRoot.childCount; i++)
                {
                    assetItems[i] = assetRoot.GetChild(i);
                }

                // Assign the items to correct places
                if (m_installItems)
                {
                    m_installLog.Add("i|Installing items on avatar");
                    AssignItemsToAvatar(assetItems);
                    m_assetItem = GetAssetItemOnAvatar(assetConfig);
                }

                // Merge animations
                if (m_installAnimator)
                {
                    m_installLog.Add("i|Merging asset FX animations");
                    MergeFXAnimators(assetConfig);
                }

                // Merge expression parameters
                if (m_installParameters)
                {
                    m_installLog.Add("i|Merging asset expression parameters");
                    MergeExpressionParameters();
                }

                // Merge expressions menu
                if (m_installMenu)
                {
                    m_installLog.Add("i|Merging asset expressions menu");
                    MergeExpressionsMenu();
                }

                // Disable the item
                if (m_assetItem != null)
                {
                    m_assetItem.gameObject.SetActive(false);
                }

                success = true;
            }
            finally
            {
                // Clean up install
                m_installLog.Add("i|Cleaning up");
                if (m_assetPrefab != null)
                {
                    DestroyImmediate(m_assetPrefab);
                }

                // Delete temporary folder
                if (Directory.Exists(INSTALLER_TEMP_FOLDER))
                {
                    File.Delete($"{INSTALLER_TEMP_FOLDER}.meta");
                    Directory.Delete(INSTALLER_TEMP_FOLDER, true);
                }

                // Save everything
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Reset flag
                m_installing = false;
            }

            if (success)
            {
                m_installLog.Add($"s|{assetConfig.AssetName} installed successfully!");
                m_moveRotateScale = EditorUtility.DisplayDialog(
                    "Okaeri Asset Installer",
                    $"{assetConfig.AssetName} has been successfully installed on {m_avatar.name}!\n\n" +
                    "The next step will guide you into correctly positioning and scaling the asset items.",
                    "Continue");
            }
        }

        /// <summary>
        /// Assigns (places) the asset items on the currently selected avatar.
        /// </summary>
        /// <param name="items">The list of asset items to assign.</param>
        private void AssignItemsToAvatar(Transform[] items)
        {
            // Check the given items
            if (items == null || items.Length == 0)
            {
                throw new ArgumentNullException("Cannot assign the items to the avatar: Invalid or empty asset items.");
            }

            // Check for the Items object and create it if it doesn't exist
            var avatarTransform = m_avatar.gameObject.transform;
            var avatarItems = avatarTransform.Find("Items");
            if (avatarItems == null)
            {
                avatarItems = new GameObject("Items").transform;
                avatarItems.SetParent(avatarTransform);
                avatarItems.SetAsLastSibling();
            }

            // Go through the items
            foreach (var assetItem in items)
            {
                // Check for the asset items container
                if (assetItem.name.EndsWith("Items"))
                {
                    m_installLog.Add($"i|\tInstalling asset into the Items container");
                    AssignAssetToContainer(assetItem);
                    continue;
                }

                // Get the armature bone the asset is supposed to be assigned to
                var bone = m_itemBones.FirstOrDefault(c => assetItem.name.EndsWith(c.Key)).Value;

                // Check if the bone is the default one
                if (bone == HumanBodyBones.Hips)
                {
                    continue;
                }

                // Install the armature asset on the bone
                m_installLog.Add($"i|\tInstalling asset into the armature {bone}");
                AssignAssetToBone(assetItem, bone);
            }
        }

        /// <summary>
        /// Assigns the main asset GameObject to the avatar Items container.
        /// </summary>
        /// <param name="item">The asset GameObject to assign.</param>
        private void AssignAssetToContainer(Transform item)
        {
            // Check the given object
            string errorMessage;
            if (item == null)
            {
                errorMessage = "Error assigning the asset to the Items container: Invalid asset item.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Check if the given item is already assigned on the avatar
            var itemsContainer = m_avatar.transform.Find(item.name);
            if (itemsContainer == null)
            {
                item.SetParent(m_avatar.transform, true);
                return;
            }

            // Check if the prefab exists on the container
            if (itemsContainer.Find(m_selectedAssetConfig.AssetItemName))
            {
                errorMessage = "Error assigning the asset to the Items container: Asset already installed. Uninstall the asset and try again.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Assign the prefab items to the container
            for (var i = 0; i < item.childCount; i++)
            {
                var child = item.GetChild(i);
                child.SetParent(m_avatar.gameObject.transform.Find(item.name), true);
            }
        }

        /// <summary>
        /// Assigns the specified asset item to the given armature bone.
        /// </summary>
        /// <param name="item">The asset item to assign.</param>
        /// <param name="bone">The armature bone to assign to.</param>
        private void AssignAssetToBone(Transform item, HumanBodyBones bone)
        {
            // Check the given asset item
            string errorMessage;
            if (item == null)
            {
                errorMessage = "Error assigning the asset to the armature: Invalid asset item.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Check if the given item is already assigned on the avatar
            var boneTransform = m_animator.GetBoneTransform(bone);
            if (boneTransform.Find(item.name) == null)
            {
                item.SetParent(boneTransform);
                item.localPosition = Vector3.zero;
                return;
            }

            // The asset is already installed on the bone
            errorMessage = "Error assigning the asset to the armature: Asset already installed. Uninstall the asset and try again.";
            m_installLog.Add($"e|{errorMessage}");
            throw new InvalidOperationException(errorMessage);
        }

        /// <summary>
        /// Merge the asset animators with the avatar one.
        /// </summary>
        /// <param name="assetConfig">The asset configuration.</param>
        private void MergeFXAnimators(OkaeriAssetConfig assetConfig)
        {
            string errorMessage;

            // Get the avatar FX animation layer index
            var fxAnimLayerIndex = GetAvatarFxAnimationLayerIndex();
            if (fxAnimLayerIndex == -1)
            {
                errorMessage =
                    "Cannot merge the FX animator controllers: Could not get the avatar FX animation layer index.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Get the avatar FX animator controller
            var fxAnimLayer = m_avatar.baseAnimationLayers[fxAnimLayerIndex];
            var avatarFxAnimatorController = fxAnimLayer.animatorController as AnimatorController;
            if (avatarFxAnimatorController == null)
            {
                errorMessage =
                    "Cannot merge the FX animator controllers: Could not get the avatar FX animator controller.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Get the asset FX animator controller
            var assetFxAnimatorControllerName =
                m_fxAnimatorWD ? assetConfig.AssetFXAnimatorWDOn : assetConfig.AssetFXAnimatorWDOff;
            var assetFxAnimatorControllerPath = Path.Combine(assetConfig.AssetPath, assetFxAnimatorControllerName);
            var assetFxAnimatorController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(assetFxAnimatorControllerPath);
            if (assetFxAnimatorController == null)
            {
                errorMessage =
                    $"Cannot merge the FX animator controllers: Could not get the asset FX animator controller {assetFxAnimatorControllerName}.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // TODO: Smartify function to automatically detect mixed WDs
            // Merge the FX animator controllers
            var mergedFxAnimController = MergeFXAnimatorControllers(avatarFxAnimatorController, assetFxAnimatorController);
            var mergedFxAnimControllerPath = AssetDatabase.GetAssetPath(mergedFxAnimController);

            // Replace the original avatar FX animator controller
            var avatarFxAnimControllerPath = AssetDatabase.GetAssetPath(avatarFxAnimatorController);
            AssetDatabase.DeleteAsset(avatarFxAnimControllerPath);
            AssetDatabase.CopyAsset(mergedFxAnimControllerPath, avatarFxAnimControllerPath);
            AssetDatabase.Refresh();

            // Update the reference on the avatar
            m_avatar.baseAnimationLayers[fxAnimLayerIndex].isDefault = false;
            m_avatar.baseAnimationLayers[fxAnimLayerIndex].animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(avatarFxAnimControllerPath);
        }

        /// <summary>
        /// Merges the two specified FX animator controllers.
        /// </summary>
        /// <param name="avatarController">The avatar FX animator controller.</param>
        /// <param name="assetController">The asset FX animator controller.</param>
        private AnimatorController MergeFXAnimatorControllers(AnimatorController avatarController, AnimatorController assetController)
        {
            // Create the merged animator controller
            var mergedFxAnimator = GetMergedFXAnimator(avatarController, INSTALLER_TEMP_FOLDER);
            if (mergedFxAnimator == null)
            {
                var errorMessage = "Error merging the FX animators: Could not get the merged FX animator controller.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Merge the animator controllers
            m_installLog.Add("i|\tGetting current avatar FX layers");
            var avatarFXParameters = avatarController.parameters.ToDictionary(p => p.name, p => p.name);
            mergedFxAnimator = AnimatorCloner.MergeControllers(mergedFxAnimator, avatarController, avatarFXParameters);

            m_installLog.Add("i|\tMerging asset FX layers");
            var assetFXParameters = assetController.parameters.ToDictionary(p => p.name, p => p.name);
            mergedFxAnimator = AnimatorCloner.MergeControllers(mergedFxAnimator, assetController, assetFXParameters);

            // Return the merged result
            AssetDatabase.SaveAssets();
            return mergedFxAnimator;
        }

        /// <summary>
        /// Gets the merged animator controller 
        /// </summary>
        /// <param name="original">The original avatar FX animator controller.</param>
        /// <param name="tempPath">The temporary path for the asset install.</param>
        /// <returns>A new animator controller on which to merge the asset FX animator controller.</returns>
        private AnimatorController GetMergedFXAnimator(AnimatorController original, string tempPath)
        {
            // Check the given original animator
            if (original == null)
            {
                var errorMessage = "Cannot get the merged FX animator: Invalid avatar animator provided.";
                m_installLog.Add($"e|{errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }

            // Get the path of the original FX animator controller
            var originalFxAnimatorPath = AssetDatabase.GetAssetPath(original);

            // Create the temporary folder for the merged FX animator controller
            var mergedFxAnimatorPath = Path.Combine(tempPath, Path.GetFileName(originalFxAnimatorPath));
            if (!AssetDatabase.IsValidFolder(tempPath))
            {
                CreateFolder(tempPath);
            }

            // Return the merged FX animator controller
            AssetDatabase.CreateAsset(new AnimatorController(), mergedFxAnimatorPath);
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(mergedFxAnimatorPath);
        }

        /// <summary>
        /// Merges the asset expression parameters with the avatar.
        /// </summary>
        private void MergeExpressionParameters()
        {
            // Merge the expression parameters
            var mergedParameters = new List<VRCExpressionParameters.Parameter>(m_avatarExpressionParameters.parameters);
            foreach (var parameter in m_assetExpressionParameters.parameters)
            {
                // Check if the parameter already exists on the avatar
                var existingParameter = m_avatarExpressionParameters.FindParameter(parameter.name);
                if (existingParameter != null)
                {
                    // Get the parameter index
                    var index = mergedParameters.IndexOf(existingParameter);

                    // Update the existing parameter settings
                    mergedParameters[index].defaultValue = parameter.defaultValue;
                    mergedParameters[index].saved = parameter.saved;
                    mergedParameters[index].networkSynced = parameter.networkSynced;
                    continue;
                }

                // Add the parameter to the avatar
                mergedParameters.Add(new VRCExpressionParameters.Parameter
                {
                    name = parameter.name,
                    valueType = parameter.valueType,
                    defaultValue = parameter.defaultValue,
                    networkSynced = parameter.networkSynced,
                    saved = parameter.saved
                });
            }

            // Set the merged expression parameters
            m_avatar.expressionParameters.parameters = mergedParameters.ToArray();

            // Mark as dirty to force save
            EditorUtility.SetDirty(m_avatar.expressionParameters);
        }

        /// <summary>
        /// Adds the asset menu to the avatar one.
        /// </summary>
        private void MergeExpressionsMenu()
        {
            // Check the expression menu
            if (m_avatarExpressionsMenu == null)
            {
                return;
            }

            // Add the expression menu control
            m_avatarExpressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = m_selectedAssetConfig.AssetName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = m_assetExpressionsMenu
            });

            // Mark as dirty to force save
            EditorUtility.SetDirty(m_avatarExpressionsMenu);
        }

        #endregion

        #region Move / Rotate / Scale Logic

        /// <summary>
        /// The list of item transforms on the avatar that can be moved / rotated.
        /// </summary>
        private Transform[] m_itemsToMoveRotate;

        /// <summary>
        /// A dictionary representing the ParentConstraint sources to use for previewing when moving the item transforms.
        /// </summary>
        private Dictionary<string, ParentConstraint> m_itemsToMoveRotateConstraints;

        /// <summary>
        /// A dictionary containing the initial asset item ParentConstraint weights.
        /// </summary>
        private Dictionary<ParentConstraint, float[]> m_parentConstraintsBackup;

        /// <summary>
        /// The list of item transforms on the avatar that can be scaled.
        /// </summary>
        private Transform[] m_itemsToScale;

        /// <summary>
        /// The initial tool before manipulating asset items.
        /// </summary>
        private Tool m_initialTool;

        /// <summary>
        /// The inital selected GameObject before manipulating asset items.
        /// </summary>
        private GameObject m_initialSelectedGameObject;

        /// <summary>
        /// Draws the Okaeri asset installer move / rotate helper.
        /// </summary>
        private void DrawAssetMoveRotateScale()
        {
            m_initialSelectedGameObject = Selection.activeGameObject;
            m_initialTool = Tools.current;
            if (GUILayout.Button("FINISH INSTALL", GUILayout.Height(32), GUILayout.ExpandWidth(true)))
            {
                Selection.activeGameObject = m_initialSelectedGameObject;
                Tools.current = m_initialTool;

                m_itemsToMoveRotate = null;
                m_itemsToScale = null;
                m_assetItem?.gameObject.SetActive(false);
                m_moveRotateScale = false;

                foreach (var parentConstraintBackup in m_parentConstraintsBackup)
                {
                    var parentConstraint = parentConstraintBackup.Key;
                    var parentConstraintWeights = parentConstraintBackup.Value;
                    for (var i = 0; i < parentConstraint.sourceCount; i++)
                    {
                        var source = parentConstraint.GetSource(i);
                        source.weight = parentConstraintWeights[i];
                        parentConstraint.SetSource(i, source);
                    }
                }

                return;
            }

            var titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontStyle = FontStyle.Bold
            };
            var labelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(5, 0, 0, 0)
            };

            // Get the asset items
            m_assetItem = GetAssetItemOnAvatar(m_selectedAssetConfig);
            if (m_assetItem == null)
            {
                EditorGUILayout.HelpBox(
                    $"Could not find the {m_selectedAssetConfig.AssetItemName} item on the selected avatar. Is the {m_selectedAssetConfig.AssetName} installed?",
                    MessageType.Error);
                GUILayout.FlexibleSpace();
                return;
            }
            m_assetItem.gameObject.SetActive(true);

            var itemsParent = m_selectedAssetConfig.AssetItemName;
            if (m_itemsToMoveRotate == null)
            {
                // Get the items to move / rotate on the avatar
                m_itemsToMoveRotate = m_selectedAssetConfig.MovableItems
                    .Select(i => GetAvatarItemByPath(i, itemsParent))
                    .Where(i => i != null)
                    .ToArray();

                // Get the ParentConstraints from the avatar item
                var parentConstraints = m_assetItem.GetComponentsInChildren<ParentConstraint>();
                m_parentConstraintsBackup = parentConstraints
                    .Select(p => new Tuple<ParentConstraint, float[]>(p, GetParentConstraintWeights(p)))
                    .ToDictionary(p => p.Item1, p => p.Item2);
                m_itemsToMoveRotateConstraints = m_itemsToMoveRotate
                    .Select(i => new Tuple<string, ParentConstraint>(i.name, GetItemParentConstraint(i.name, parentConstraints)))
                    .ToDictionary(i => i.Item1, i => i.Item2);
            }
            if (m_itemsToScale == null)
            {
                // Get the items to scale on the avatar
                m_itemsToScale = m_selectedAssetConfig.ScalableItems
                    .Select(i => GetAvatarItemByPath(i, itemsParent))
                    .Where(i => i != null)
                    .ToArray();
            }

            // Draw the layouts
            EditorGUILayout.LabelField("MOVE / ROTATE:", titleStyle);
            EditorGUILayout.HelpBox("It is very important to only change the POSITION and ROTATION of the items selected!", MessageType.Warning);
            GUILayout.Space(8);
            foreach (var item in m_itemsToMoveRotate)
            {
                DrawMoveScaleLayout(item, labelStyle, Tool.Move, Tool.Rotate);
            }

            GUILayout.Space(16);

            EditorGUILayout.LabelField("SCALE:", titleStyle);
            EditorGUILayout.HelpBox("It is very important to only change the SCALE of the items selected!", MessageType.Warning);
            GUILayout.Space(8);
            foreach (var item in m_itemsToScale)
            {
                DrawMoveScaleLayout(item, labelStyle, Tool.Scale);
            }

            GUILayout.FlexibleSpace();
        }

        /// <summary>
        /// Get the ParentConstraint weights.
        /// </summary>
        /// <param name="parentConstraint">The ParentConstraint to get the weights for.</param>
        /// <returns>A list of weights representing the value for each ParentConstraint source index.</returns>
        private float[] GetParentConstraintWeights(ParentConstraint parentConstraint)
        {
            // Check the given ParentConstraint
            if (parentConstraint == null || parentConstraint.sourceCount == 0)
            {
                return null;
            }

            // Get the weights
            var result = new float[parentConstraint.sourceCount];
            for (var i = 0; i < parentConstraint.sourceCount; i++)
            {
                result[i] = parentConstraint.GetSource(i).weight;
            }

            // Return the result
            return result;
        }

        /// <summary>
        /// Gets the ParentConstraint for the specified asset item to move / rotate.
        /// </summary>
        /// <param name="itemName">The name of the asset item to move / rotate.</param>
        /// <param name="parentConstraints">The list of ParentConstraints on the asset item.</param>
        /// <returns>The ParentConstraint for the specified asset item to move / rotate.</returns>
        private ParentConstraint GetItemParentConstraint(string itemName, ParentConstraint[] parentConstraints)
        {
            // Check the given arguments
            if (string.IsNullOrWhiteSpace(itemName) || parentConstraints == null || parentConstraints.Length == 0)
            {
                return null;
            }

            // Find the constraint source
            foreach (var parentConstraint in parentConstraints)
            {
                for (var i = 0; i < parentConstraint.sourceCount; i++)
                {
                    var source = parentConstraint.GetSource(i);
                    if (source.sourceTransform != null && source.sourceTransform.name.Equals(itemName))
                    {
                        return parentConstraint;
                    }
                }
            }

            // Return nothing if not found
            return null;
        }

        /// <summary>
        /// Draws the layout for the asset item to move / rotate / scale.
        /// </summary>
        /// <param name="item">The item to manipulate.</param>
        /// <param name="tool">Determines the item  manipulation.</param>
        /// <param name="labelStyle">The label style.</param>
        private void DrawMoveScaleLayout(Transform item, GUIStyle labelStyle, params Tool[] tools)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(item.name, labelStyle, GUILayout.ExpandWidth(true));

            foreach (var tool in tools)
            {
                if (GUILayout.Button(tool.ToString(), GUILayout.Width(100)))
                {
                    // Select the object
                    Selection.activeGameObject = item.gameObject;
                    EditorGUIUtility.PingObject(item.gameObject);
                    Tools.current = tool;

                    // ParentConstraint preview
                    if (m_itemsToMoveRotateConstraints.TryGetValue(item.name, out var parentConstraint) && parentConstraint != null)
                    {
                        // Go thorugh the parent constraint sources
                        for (var i = 0; i < parentConstraint.sourceCount; i++)
                        {
                            var source = parentConstraint.GetSource(i);
                            source.weight = source.sourceTransform.name.Equals(item.name) ? 1 : 0;
                            parentConstraint.SetSource(i, source);
                        }
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
        }

        /// <summary>
        /// Returns the Transform for the avatar item with the specified name.
        /// </summary>
        /// <param name="itemName">The name of the item.</param>
        /// <param name="itemParent">The asset item name.</param>
        /// <returns></returns>
        private Transform GetAvatarItemByPath(string itemName, string itemParent)
        {
            // Check if the item is an asset item
            if (itemName.StartsWith(itemParent))
            {
                // Get the avatar Items transform
                var assetItems = m_avatar.transform.Find("Items");
                if (assetItems == null)
                {
                    throw new InvalidOperationException("no Items on avatar");
                }

                // Return the asset item
                var item = assetItems.Find(itemName.Replace("\\", "/"));
                return item;
            }

            // Check if the item is an armature item
            var bone = m_itemBones.FirstOrDefault(c => itemName.EndsWith(c.Key)).Value;
            if (bone.Equals(HumanBodyBones.Hips))
            {
                throw new InvalidOperationException("not on avatar armature");
            }

            // Return the armature item
            return m_animator.GetBoneTransform(bone).Find(itemName);
        }

        #endregion

        #region Uninstaller Logic

        /// <summary>
        /// Removes the selected Okaeri asset from the current avatar.
        /// </summary>
        /// <param name="assetConfig">The asset configuration.</param>
        private void UninstallAsset(OkaeriAssetConfig assetConfig)
        {
            m_installLog.Add($"i|Uninstalling {assetConfig.AssetName} from the avatar");

            // Remove the asset items from the avatar
            m_installLog.Add($"i|\tRemoving items");
            var assetItemsOnAvatar = GetAssetItemsOnAvatar(assetConfig);
            foreach (var assetItem in assetItemsOnAvatar)
            {
                m_installLog.Add($"i|\t\t{assetItem.name}");
                DestroyImmediate(assetItem.gameObject);
            }

            // Remove the Items gameobject if empty
            var avatarTransform = m_avatar.gameObject.transform;
            var avatarItems = avatarTransform.Find("Items");
            if (avatarItems != null)
            {
                if (avatarItems.GetComponentsInChildren<Transform>(true).Length <= 1)
                {
                    DestroyImmediate(avatarItems.gameObject);
                }
            }

            // Remove the FX animator layers and parameters
            m_installLog.Add($"i|\tRemoving FX animator layers and parameters");
            var avatarFxAnimLayerIndex = GetAvatarFxAnimationLayerIndex();
            if (avatarFxAnimLayerIndex != -1)
            {
                var avatarFxAnimController =
                    m_avatar.baseAnimationLayers[avatarFxAnimLayerIndex].animatorController as AnimatorController;
                if (avatarFxAnimController != null)
                {
                    var assetLayersAndParametersOnAvatar = GetAssetAnimatorLayersAndParametersOnAvatar(assetConfig);

                    var avatarFxAnimLayerNames = avatarFxAnimController.layers.Select(l => l.name);
                    var cleanAvatarFxAnimLayerNames = avatarFxAnimLayerNames.Except(assetLayersAndParametersOnAvatar.Item1);
                    var cleanAvatarFxAnimLayers =
                        avatarFxAnimController.layers.Where(l => cleanAvatarFxAnimLayerNames.Contains(l.name)).ToArray();

                    var avatarFxAnimParameterNames = avatarFxAnimController.parameters.Select(p => p.name);
                    var cleanAvatarFxAnimParameterNames =
                        avatarFxAnimParameterNames.Except(assetLayersAndParametersOnAvatar.Item2);
                    var cleanAvatarFxAnimParameters =
                        avatarFxAnimController.parameters.Where(p => cleanAvatarFxAnimParameterNames.Contains(p.name)).ToArray();

                    avatarFxAnimController.layers = cleanAvatarFxAnimLayers;
                    avatarFxAnimController.parameters = cleanAvatarFxAnimParameters;

                    m_avatar.baseAnimationLayers[avatarFxAnimLayerIndex].animatorController = avatarFxAnimController;
                }
            }

            // Remove the expression parameters
            m_installLog.Add($"i|\tRemoving expression parameters");
            var assetExpressionParametersOnAvatar = GetAssetExpressionParametersOnAvatar(assetConfig);
            if (assetExpressionParametersOnAvatar.Length > 0)
            {
                var avatarExpressionParametersNames = m_avatar.expressionParameters.parameters.Select(p => p.name);
                var cleanAvatarExpressionParametersNames =
                    avatarExpressionParametersNames.Except(assetExpressionParametersOnAvatar);

                m_avatar.expressionParameters.parameters =
                    m_avatar.expressionParameters.parameters.Where(p => cleanAvatarExpressionParametersNames.Contains(p.name)).ToArray();

                // Mark as dirty to force save
                EditorUtility.SetDirty(m_avatar.expressionParameters);
            }

            // Save everything
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        #endregion

    }
}
