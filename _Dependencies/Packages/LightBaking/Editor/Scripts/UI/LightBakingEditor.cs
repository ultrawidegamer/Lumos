using System;
using System.Threading.Tasks;
using LightBakingResoLink;
using ResoMeshXParsing;
using UnityEditor;
using UnityEngine;

public class LightBaking : EditorWindow {
    private bool showConnectionSettings = true;
    private bool showCacheSettings = true;
    private bool showActionSettings = true;
    private bool showLightingSettings = true;
    private bool resoLinkConnected = false;
    private bool cacheDirConnected = false;
    private bool dataDirConnected = false;
    private Texture2D resoLinkConnectIcon;
    private Texture2D resoLinkDisconnectIcon;
    private Texture2D cacheConnectIcon;
    private Texture2D cacheDisconnectIcon;
    private Texture2D actionsIcon;
    private Texture2D lightingIcon;
    private ResoLinkHelper resoLinkHelper;
    private string wsUrl = "ws://localhost:5000";

    [MenuItem("Tools/Light Baking")]
    public static void ShowWindow() {
        GetWindow<LightBaking>("Light Baking");
    }

    private void OnEnable() {
        resoLinkHelper = ResoLinkHelper.Instance;
        resoLinkConnectIcon = EditorGUIUtility.IconContent("d_Linked@2x").image as Texture2D;
        resoLinkDisconnectIcon = EditorGUIUtility.IconContent("d_Unlinked@2x").image as Texture2D;
        cacheConnectIcon = EditorGUIUtility.IconContent("d_CacheServerConnected@2x").image as Texture2D;
        cacheDisconnectIcon = EditorGUIUtility.IconContent("d_CacheServerDisconnected@2x").image as Texture2D;
        actionsIcon = EditorGUIUtility.IconContent("d_Preset.Context@2x").image as Texture2D;
        lightingIcon = EditorGUIUtility.IconContent("d_LightingSettings Icon").image as Texture2D;
        cacheDirConnected = MeshXCache.Instance.IsCacheDirConnected();
        dataDirConnected = MeshXCache.Instance.IsDataDirConnected();
    }

    private void OnGUI() {
        if (Event.current.type == EventType.MouseDown) {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
        }

        if (resoLinkHelper == null) {
            resoLinkHelper = ResoLinkHelper.Instance;
        }
     
        resoLinkConnected = resoLinkHelper.IsConnected();
        CreateConnectionSettingsGUI();
        CreateCacheSettingsGUI();
        CreateActionsGUI();
        CreateLightingGUI();
    }

    private async void CreateConnectionSettingsGUI() {
        EditorGUILayout.BeginHorizontal();
        GUIContent content = new GUIContent("Connection Settings", resoLinkConnected ? resoLinkConnectIcon : resoLinkDisconnectIcon);
        showConnectionSettings = EditorGUILayout.Foldout(showConnectionSettings, content, true);
        EditorGUILayout.EndHorizontal();

        if (showConnectionSettings) {
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("URL: ", GUILayout.Width(45));
            wsUrl = EditorGUILayout.TextField(wsUrl);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button(resoLinkConnected ? "Disconnect" : "Connect")) {
                if (resoLinkConnected) {
                    Debug.Log("Disconnecting from ResoLink...");
                    await resoLinkHelper.DisconnectAsync();
                } else {
                    Debug.Log("Trying ResoLink Connection at: " + wsUrl);
                    await resoLinkHelper.ConnectAsync(wsUrl);
                }
                Repaint();
            }
            EditorGUI.indentLevel--;
        }
    }

    private void CreateCacheSettingsGUI() {
        EditorGUILayout.BeginHorizontal();
        GUIContent content = new GUIContent("Cache Settings", cacheDirConnected && dataDirConnected ? cacheConnectIcon : cacheDisconnectIcon);
        showCacheSettings = EditorGUILayout.Foldout(showCacheSettings, content, true);
        EditorGUILayout.EndHorizontal();
        if (showCacheSettings) {
            EditorGUI.indentLevel++;
            CreateFolderPickerGUI("Cache Folder", cacheDirConnected, ref MeshXCache.Instance.cacheDirectory);
            CreateFolderPickerGUI("Data Folder", dataDirConnected, ref MeshXCache.Instance.dataDirectory);
            EditorGUI.indentLevel--;
        }
    }

    private void CreateActionsGUI() {
        EditorGUILayout.BeginHorizontal();
        GUIContent content = new GUIContent("ResoLink Actions", actionsIcon);
        showActionSettings = EditorGUILayout.Foldout(showActionSettings, content, true);
        EditorGUILayout.EndHorizontal();

        if (showActionSettings) {
            EditorGUI.indentLevel++;
            if (GUILayout.Button("Retrieve Resonite Data")) {
                Debug.Log("Try to retrieve data from Resonite");
                RetrieveMesh();
            }
            EditorGUI.indentLevel--;
        }
    }

    private void CreateLightingGUI() {
        EditorGUILayout.BeginHorizontal();
        GUIContent content = new GUIContent("Lighting Settings", lightingIcon);
        showLightingSettings = EditorGUILayout.Foldout(showLightingSettings, content, true);
        EditorGUILayout.EndHorizontal();

        if (showLightingSettings) {
            EditorGUI.indentLevel++;
            for (int i = 0; i < 5; i++) {
                if (GUILayout.Button("Placeholder Button")) {
                    Debug.Log("Try to retrieve mesh from ResoLink");
                }
            }
            EditorGUI.indentLevel--;
        }
    }

    private void CreateFolderPickerGUI(string name, bool connected, ref string path) {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label(new GUIContent(connected ? cacheConnectIcon : cacheDisconnectIcon), GUILayout.Height(20), GUILayout.Width(20));
        GUILayout.Space(-20);
        EditorGUILayout.LabelField($"{name}: ", GUILayout.Width(110), GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Browse", GUILayout.Width(100))) {
            string folder = EditorUtility.OpenFolderPanel($"Select {name}", path, "");
            if (!string.IsNullOrEmpty(folder)) {
                path = folder;
                Repaint();
                cacheDirConnected = MeshXCache.Instance.IsCacheDirConnected();
                dataDirConnected = MeshXCache.Instance.IsDataDirConnected();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private Task ProgressBar(Func<Action<string, float>, Task> func) { 
        return func((message, progress) => {
            EditorUtility.DisplayProgressBar("Light Baking", message, Math.Clamp(progress, 0f, 1f));
        }).ContinueWith(_ => Task.Delay(100));
    }

    private async void RetrieveMesh() {
        try {
            await ProgressBar(MeshXCache.Instance.UpdatePathCache);
            await ProgressBar(resoLinkHelper.FetchMeshSlots);
            await ProgressBar(resoLinkHelper.BuildLookupTables);
            await ProgressBar(resoLinkHelper.ApplyTRSToObjects);

            await resoLinkHelper.DownloadAndApplyMeshes((message, progress, obj) => {
                EditorUtility.DisplayProgressBar("Light Baking", message, progress);
                if (obj != null) {
                    CollapseOldAndFocusNewObject(obj);
                }
            });
        } catch (Exception e) {
            Debug.LogError($"Error during mesh retrieval: {e.Message}\n{e.StackTrace}");
        } finally {
            EditorUtility.ClearProgressBar();
        }
    }

    private void CollapseOldAndFocusNewObject(GameObject obj) {
        Selection.activeGameObject = obj;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
}