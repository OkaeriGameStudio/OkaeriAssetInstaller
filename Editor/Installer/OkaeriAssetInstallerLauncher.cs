using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Net.NetworkInformation;
using Newtonsoft.Json;

namespace Okaeri.Editor
{
    /// <summary>
    /// Helper class for downloading Okaeri Asset Installer components.
    /// </summary>
    public class OkaeriAssetInstallerDownloader
    {
        private class OkaeriAssetInstallerRepositoryItem
        {
            public string Type { get; set; }
            public string URL { get; set; }
            public string Path { get; set; }
        }

        private class OkaeriAssetInstallerRepositoryBlob
        {
            public string Content { get; set; }
        }

        private class OkaeriAssetInstallerRepository
        {
            [JsonProperty("tree")]
            public OkaeriAssetInstallerRepositoryItem[] Items { get; set; }
        }

        /// <summary>
        /// The HTTPClient instance to use for requests.
        /// </summary>
        private static readonly HttpClient m_httpClient = new HttpClient();

        /// <summary>
        /// The name of the Okaeri Asset Installer update repository branch.
        /// </summary>
        private const string INSTALLER_UPDATE_REPOSITORY_BRANCH = "feat/config-materials";

        /// <summary>
        /// The Okaeri Asset Installer main update repository.
        /// </summary>
        private static readonly string INSTALLER_UPDATE_REPOSITORY = $"https://api.github.com/repos/OkaeriGameStudio/OkaeriAssetInstaller/git/trees/{INSTALLER_UPDATE_REPOSITORY_BRANCH}";

        /// <summary>
        /// The Okaeri Asset Installer update repository path.
        /// </summary>
        private static readonly string INSTALLER_REPOSITORY_PATH = "Editor/Installer";

        /// <summary>
        /// The Okaeri Asset Installer repository files.
        /// </summary>
        private static OkaeriAssetInstallerRepository m_installerRepository;

        /// <summary>
        /// Gets or sets the local Okaeri Asset Installer path.
        /// </summary>
        public static string LOCAL_INSTALLER_PATH { get; set; }

        /// <summary>
        /// Determines if download operations can be performed.
        /// </summary>
        /// <returns>True if download operations can be performed.</returns>
        public static async Task<bool> CanDownload()
        {
            // Check if the current session has network connectivity
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                Debug.LogWarning("No network connection present.");
                return false;
            }

            // Check if we have access to the internet
            var pingRequest = new System.Net.NetworkInformation.Ping();
            var pingResponse = await pingRequest.SendPingAsync("1.1.1.1", 10000);
            if (pingResponse.Status != IPStatus.Success)
            {
                Debug.LogWarning("Cannot acces Internet.");
                return false;
            }

            // We can download
            return true;
        }

        /// <summary>
        /// Returns the Okaeri Asset Installer repository items from the update repository.
        /// </summary>
        /// <param name="address">The update repository address path.</param>
        /// <returns>An OkaeriAssetInstallerRepository object.</returns>
        private static async Task<OkaeriAssetInstallerRepository> GetRepositoryItems(string address)
        {
            // Check if we can reach the repository
            if (!await CanDownload())
            {
                Debug.LogError("Cannot reach the Okaeri Asset Installer repository. Check your Internet connection and try again.");
                return null;
            }

            // Update the client
            if (m_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                m_httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("OkaeriAssetInstaller", "1"));
            }

            // Get the editor URL
            var request = await m_httpClient.GetAsync(address + "?recursive=1");
            if (!request.IsSuccessStatusCode)
            {
                Debug.LogError($"Cannot read the Okaeri Asset Installer repository: {request.ReasonPhrase}");
                return null;
            }

            // Check the response
            var response = await request.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(response))
            {
                Debug.LogError($"Invalid response received for the Okaeri Asset Installer repository.");
                return null;
            }

            // Return the result
            return JsonConvert.DeserializeObject<OkaeriAssetInstallerRepository>(response);
        }

        /// <summary>
        /// Returns the latest Okaeri Asset Installer repository.
        /// </summary>
        /// <returns>The latest Okaeri Asset Installer repository.</returns>
        private static async Task<OkaeriAssetInstallerRepository> GetInstallerRepository()
        {
            // Check if we already cached the repository
            if (m_installerRepository != null && m_installerRepository.Items.Length > 0)
            {
                return m_installerRepository;
            }

            // Get the base update repository
            m_installerRepository = await GetRepositoryItems(INSTALLER_UPDATE_REPOSITORY);
            if (m_installerRepository == null || m_installerRepository.Items.Length == 0)
            {
                Debug.LogError("Could not fetch the Okaeri Asset Installer update repository files.");
                return null;
            }

            // Return the result
            return m_installerRepository;
        }

        /// <summary>
        /// Returns the contents of a specified Okaeri Asset Installer update repository file.
        /// </summary>
        /// <param name="repositoryUrl">The repository file Uri.</param>
        /// <returns>The file bytes.</returns>
        private static async Task<byte[]> GetRepositoryFileBytes(string repositoryUrl)
        {
            // Check the given url.
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return null;
            }

            // Get the contents
            var request = await m_httpClient.GetAsync(repositoryUrl);
            if (!request.IsSuccessStatusCode)
            {
                Debug.LogError($"Cannot read the Okaeri Asset Installer repository file: {request.ReasonPhrase}");
                return null;
            }

            // Read the contents
            var response = await request.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(response))
            {
                Debug.LogError($"Invalid response received for the Okaeri Asset Installer repository.");
                return null;
            }

            // Convert the contents
            var blob = JsonConvert.DeserializeObject<OkaeriAssetInstallerRepositoryBlob>(response);
            if (string.IsNullOrWhiteSpace(blob.Content))
            {
                return null;
            }

            // Return the result
            return Convert.FromBase64String(blob.Content);
        }

        /// <summary>
        /// Retrieves the latest Okaeri Asset Installer files at the specified repository path.
        /// </summary>
        /// <param name="repositoryPath">The repository files path.</param>
        /// <returns>True if the Okaeri Asset Installer files at the specified repository path have been updated.</returns>
        private static async Task<bool> GetLatestRepositoryFiles(string repositoryPath)
        {
            // Check the installer path
            if (string.IsNullOrWhiteSpace(LOCAL_INSTALLER_PATH))
            {
                Debug.LogError("Could not get the latest installer file: The local installer path is empty or invalid.");
                return false;
            }

            // Check the repository path
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                Debug.LogError("Could not get the latest installer file: The provided repository path is empty or invalid.");
                return false;
            }

            // Get the latest repository files
            var installerRepository = await GetInstallerRepository();
            var repositoryFilesPath = INSTALLER_REPOSITORY_PATH + $"/{repositoryPath}";
            var repositoryFiles = installerRepository.Items.Where(i => i.Type.Equals("blob") && i.Path.StartsWith(repositoryFilesPath));
            if (!repositoryFiles.Any())
            {
                Debug.LogWarning($"Could not get the latest Okaeri Asset Installer repository files at: {repositoryFilesPath}.");
                return false;
            }

            // Retrieve the latest files
            try
            {
                var localPath = LOCAL_INSTALLER_PATH.Replace(INSTALLER_REPOSITORY_PATH.Replace("/", "\\"), "");
                foreach (var repositoryFile in repositoryFiles)
                {
                    // Get the local installer path
                    var currentPath = Path.Combine(localPath, repositoryFile.Path).Replace("/", "\\");

                    // Update the file
                    var bytes = await GetRepositoryFileBytes(repositoryFile.URL);
                    File.WriteAllBytes(currentPath, bytes);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
                return false;
            }
        }

        #region Resources

        /// <summary>
        /// The relative Okaeri Asset Installer update repository path to the resources.
        /// </summary>
        private const string RESOURCES_PATH = "Resources";

        /// <summary>
        /// Gets the latest Okaeri Asset Installer resources.
        /// </summary>
        /// <returns>True if the Okaeri Asset Installer resources have been updated.</returns>
        public static async Task<bool> GetLatestResources()
        {
            return await GetLatestRepositoryFiles(RESOURCES_PATH);
        }

        #endregion

        #region Asset Configurations

        /// <summary>
        /// The Okaeri asset configuration schema file name.
        /// </summary>
        private const string ASSET_CONFIG_FILE_NAME = "OkaeriAssetConfig.cs";

        /// <summary>
        /// The Okaeri asset configurations path.
        /// </summary>
        private const string ASSET_CONFIGS_PATH = "Configs";

        /// <summary>
        /// Gets the latest Okaeri asset configurations.
        /// </summary>
        /// <returns>True if the Okaeri asset configurations have been updated.</returns>
        public static async Task<bool> GetLatestAssetConfigurations()
        {
            var assetConfig = await GetLatestRepositoryFiles(ASSET_CONFIG_FILE_NAME);
            var assetConfigs = await GetLatestRepositoryFiles(ASSET_CONFIGS_PATH);
            return assetConfig && assetConfigs;
        }

        #endregion
    }

    /// <summary>
    /// Class responsible for updating and launching the Okaeri Asset Installer window.
    /// </summary>
    public class OkaeriAssetInstallerLauncher : EditorWindow
    {
        /// <summary>
        /// The Okaeri Asset Installer file name.
        /// </summary>
        private const string INSTALLER_FILE_NAME = "OkaeriAssetInstaller.cs";

        /// <summary>
        /// The number of characters for identifying the version.
        /// </summary>
        private const int INSTALLER_VERSION_SIZE = 14;

        /// <summary>
        /// Window title.
        /// </summary>
        private const string WINDOW_TITLE = "Okaeri Asset Installer";

        /// <summary>
        /// The Okaeri Asset Installer window width.
        /// </summary>
        private const int WINDOW_WIDTH = 360;

        /// <summary>
        /// The Okaeri Asset Installer window height.
        /// </summary>
        private const int WINDOW_HEIGHT = 500;

        /// <summary>
        /// The reference to the currently open Okaeri Asset Installer window.
        /// </summary>
        private static EditorWindow m_installerWindow;

        /// <summary>
        /// The reference to the Okaeri Asset Installer launcher window.
        /// </summary>
        private static OkaeriAssetInstallerLauncher m_launcherWindow;

        /// <summary>
        /// Okaeri Asset Installer Unity menu entry.
        /// </summary>
        [MenuItem("Okaeri/Asset Installer")]
        public static void ShowWindow()
        {
            m_launcherWindow = GetWindow<OkaeriAssetInstallerLauncher>();

            const int height = WINDOW_WIDTH / 4;
            var x = (Screen.currentResolution.width - WINDOW_WIDTH) / 2;
            var y = (Screen.currentResolution.height - height) / 2;
            m_launcherWindow.position = new Rect(x, y, WINDOW_WIDTH, height);
            m_launcherWindow.Show();
        }

        /// <summary>
        /// The path to the Okaeri Asset Installer script path.
        /// </summary>
        private string m_installerScriptPath;

        /// <summary>
        /// The current Okaeri Asset Installer version.
        /// </summary>
        private Version m_installerVersion = new Version(0, 0, 0);

        /// <summary>
        /// The latest Okaeri Asset Installer version.
        /// </summary>
        private Version m_latestInstallerVersion;

        /// <summary>
        /// The Okaeri Asset Installer launcher status text.
        /// </summary>
        private string m_launcherStatus;

        /// <summary>
        /// Determines if the window needs to be closed.
        /// </summary>
        private bool m_closeWindow;

        /// <summary>
        /// Handles the window initialization.
        /// </summary>
        private void OnEnable()
        {
            GetInstallerScriptPath();
            if (CheckForDependencies())
            {
                CheckForUpdates();
                return;
            }

            m_closeWindow = true;
        }

        /// <summary>
        /// Handles the window drawing.
        /// </summary>
        private void OnGUI()
        {
            // Check if we close the window
            if (m_closeWindow)
            {
                Close();
                return;
            }

            // Check if the installer window is shown
            if (m_installerWindow != null)
            {
                Close();
                return;
            }

            // Display any messages
            var isError = false;
            var status = string.Empty;
            if (!string.IsNullOrWhiteSpace(m_launcherStatus))
            {
                isError = m_launcherStatus.StartsWith("e!");
                status = m_launcherStatus.Substring(isError ? 2 : 0);
            }
            EditorGUILayout.HelpBox(status, isError ? MessageType.Error : MessageType.Info, true);

            // Display the versions
            GUI.enabled = false;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Current version:");
            GUILayout.TextField(m_installerVersion.ToString(), GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Latest version:");
            GUILayout.TextField(m_latestInstallerVersion?.ToString(), GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        /// <summary>
        /// Gets the path to the Okaeri Asset Installer script file.
        /// </summary>
        private void GetInstallerScriptPath()
        {
            var launcherPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            string errorMessage;
            if (string.IsNullOrWhiteSpace(launcherPath))
            {
                errorMessage = "Could not get the launcher script path.";
                m_launcherStatus = "e!" + errorMessage;
                throw new InvalidOperationException(errorMessage);
            }

            var launcherDirectory = Path.GetDirectoryName(launcherPath);
            if (string.IsNullOrWhiteSpace(launcherDirectory))
            {
                errorMessage = "Could not get the launcher script directory.";
                m_launcherStatus = "e!" + errorMessage;
                throw new InvalidOperationException(errorMessage);
            }

            m_installerScriptPath = Path.Combine(launcherDirectory, INSTALLER_FILE_NAME);
        }

        /// <summary>
        /// Checks for Okaeri Asset Installer dependencies
        /// </summary>
        private bool CheckForDependencies()
        {
            // Check for VRLabs AV3 Manager
            var vpmManifest = "Packages/vpm-manifest.json";
            if (File.Exists(vpmManifest))
            {
                var vpmPackages = File.ReadAllText(vpmManifest);
                if (!vpmPackages.Contains("dev.vrlabs.av3manager"))
                {
                    EditorUtility.DisplayDialog("Okaeri Asset Installer Dependencies",
                        "The Okaeri Asset Installer requires VRLabs AV3 Manager package to be installed.\n\nPlease install the VRLabs AV3 Manager package and try again!",
                        "OK");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks for updates to the Okaeri Asset Installer.
        /// </summary>
        private async void CheckForUpdates()
        {
            m_launcherStatus = "Checking for updates";
            string versionLine, versionString;
            bool offlineMode = false;

            // Check if the window is currently installed
            var installerScript = AssetDatabase.LoadAssetAtPath<TextAsset>(m_installerScriptPath);
            if (installerScript != null && !string.IsNullOrWhiteSpace(installerScript.text))
            {
                m_launcherStatus = "Getting current version";
                versionLine = installerScript.text.Substring(0, INSTALLER_VERSION_SIZE);
                versionString = versionLine.Replace("//VERSION", "");
                m_installerVersion = Version.Parse(versionString);
            }

            // Try to update the script
            try
            {
                // Get the latest installer
                m_launcherStatus = "Getting latest version";
                var latestInstallerScriptText = await GetLatestInstaller();
                if (string.IsNullOrWhiteSpace(latestInstallerScriptText))
                {
                    var errorMessage = "Could not get the latest installer: Empty or invalid installer content.";
                    m_launcherStatus = "e!" + errorMessage;
                    throw new InvalidOperationException(errorMessage);
                }

                // Get the latest installer version
                versionLine = latestInstallerScriptText.Substring(0, INSTALLER_VERSION_SIZE);
                versionString = versionLine.Replace("//VERSION", "");
                m_latestInstallerVersion = Version.Parse(versionString);

                // Check if we need to update
                if (m_installerVersion >= m_latestInstallerVersion)
                {
                    ShowInstaller(true);
                    return;
                }

                // Update the asset
                m_launcherStatus = "Installing latest version";
                AssetDatabase.DeleteAsset(m_installerScriptPath);
                File.WriteAllText(m_installerScriptPath, latestInstallerScriptText);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.SaveAssets();

                // Wait for code compilation
                await Task.Run(() => Thread.Sleep(2000));
            }
            catch
            {
                // Logs if the server is dead or no internet
                m_launcherStatus = "Couldn't connect to the server!";
                offlineMode = true;
                Debug.Log("<color=pink>[Okaeri]" + "Couldn't connect to the server! Proceeding without installer updates... Version: " + m_installerVersion);
            }

            // Show the installer
            ShowInstaller(offlineMode);
        }

        /// <summary>
        /// Gets the latest version of the Okaeri Asset Installer.
        /// </summary>
        /// <returns>The latest version of the Okaeri Asset Installer.</returns>
        private async Task<string> GetLatestInstaller()
        {
            using (var client = new HttpClient())
            {
                var stringContents = await client.GetStringAsync("https://rammyvps.vps.webdock.cloud/api/installer");
                return stringContents.Replace("\n", Environment.NewLine);
            }
        }

        /// <summary>
        /// Shows the Okaeri Asset Installer window.
        /// </summary>
        /// <param name="offlineMode">Determines if the Okaeri Asset Installer can update.</param>
        private void ShowInstaller(bool offlineMode)
        {
            // Prepare the window position
            var windowX = (Screen.currentResolution.width - WINDOW_WIDTH) / 2;
            var windowY = (Screen.currentResolution.height - WINDOW_HEIGHT) / 2;

            // Instantiate the window
            m_installerWindow = CreateInstance("OkaeriAssetInstaller") as EditorWindow;
            if (m_installerWindow != null)
            {
                var title = offlineMode ? $"{WINDOW_TITLE} [OFFLINE]" : WINDOW_TITLE;
                m_installerWindow.titleContent = new GUIContent(title);
                m_installerWindow.position = new Rect(windowX, windowY, WINDOW_WIDTH, WINDOW_HEIGHT);
                m_installerWindow.Show();
            }
        }
    }
}
