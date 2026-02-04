using UnityEditor;
using UnityEngine;
using LightBakingResoLink;

public class LightBaking : EditorWindow {
    private bool showConnectionSettings = true;
    private bool showActionSettings = true;
    private bool showLightingSettings = true;
    private bool connected = false;
    private Texture2D connectIcon;
    private Texture2D disconnectIcon;
    private Texture2D actionsIcon;
    private Texture2D lightingIcon;
    private string wsUrl = "ws://127.0.0.1:5000";
    private ResoLinkHelper resoLinkHelper;

    [MenuItem("Tools/Light Baking")]
    public static void ShowWindow() {
        GetWindow<LightBaking>("Light Baking");
    }

    private void OnEnable() {
        resoLinkHelper = ResoLinkHelper.Instance;

        connectIcon = EditorGUIUtility.IconContent("d_Linked@2x").image as Texture2D;
        disconnectIcon = EditorGUIUtility.IconContent("d_Unlinked@2x").image as Texture2D;
        actionsIcon = EditorGUIUtility.IconContent("d_Preset.Context@2x").image as Texture2D;
        lightingIcon = EditorGUIUtility.IconContent("d_LightingSettings Icon").image as Texture2D;
    }

    private void OnGUI() {
        if (Event.current.type == EventType.MouseDown) {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
        }

        if (resoLinkHelper == null) {
            resoLinkHelper = ResoLinkHelper.Instance;
        }
     
        connected = resoLinkHelper.IsConnected();
            
        CreateConnectionSettingsGUI();
        CreateActionsGUI();
        CreateLightingGUI();
    }

    private async void CreateConnectionSettingsGUI() {
        EditorGUILayout.BeginHorizontal();
            GUIContent content = new GUIContent("Connection Settings", connected ? connectIcon : disconnectIcon);
            showConnectionSettings = EditorGUILayout.Foldout(showConnectionSettings, content, true);
        EditorGUILayout.EndHorizontal();

        if (showConnectionSettings) {
            EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("URL: ", GUILayout.Width(45));
                    wsUrl = EditorGUILayout.TextField(wsUrl);
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button(connected ? "Disconnect" : "Connect")) {
                    if (connected) {
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

    private void CreateActionsGUI() {
        EditorGUILayout.BeginHorizontal();
            GUIContent content = new GUIContent("ResoLink Actions", actionsIcon);
            showActionSettings = EditorGUILayout.Foldout(showActionSettings, content, true);
        EditorGUILayout.EndHorizontal();

        if (showActionSettings) {
            EditorGUI.indentLevel++;             
                if (GUILayout.Button("Retrieve Mesh")) {
                    Debug.Log("Try to retrieve mesh from ResoLink");
                    RetrieveMesh();
                }
                if (GUILayout.Button("Send Mesh")) {
                    Debug.Log("Try to send mesh through ResoLink");
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
                if (GUILayout.Button("Placeholder Button")) {
                    Debug.Log("Try to retrieve mesh from ResoLink");
                }
                if (GUILayout.Button("Placeholder Button")) {
                    Debug.Log("Try to retrieve mesh from ResoLink");
                }
                if (GUILayout.Button("Placeholder Button")) {
                    Debug.Log("Try to retrieve mesh from ResoLink");
                }
                if (GUILayout.Button("Placeholder Button")) {
                    Debug.Log("Try to retrieve mesh from ResoLink");
                }
                if (GUILayout.Button("Placeholder Button")) {
                    Debug.Log("Try to retrieve mesh from ResoLink");
                }
            EditorGUI.indentLevel--;
        }
    }

    private async void RetrieveMesh() {
        await resoLinkHelper.SendAsync(new GetSlotMessage {});
        Debug.Log(await resoLinkHelper.ReceiveAsync());
    }
}
