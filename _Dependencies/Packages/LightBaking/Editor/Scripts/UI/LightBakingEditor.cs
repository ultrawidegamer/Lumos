using System;
using System.Threading.Tasks;
using LightBakingResoLink;
using ResoMeshXParsing;
using UnityEditor;
using UnityEngine;

public class LightBaking : EditorWindow {
    private bool showConnectionSettings = false;
    private bool showCacheSettings = false;
    private bool showActionSettings = false;
    private bool showLightingSettings = false;
    private bool showTempSettings = false;
    private bool resoLinkConnected = false;
    private bool cacheDirConnected = false;
    private bool dataDirConnected = false;
    private bool transitioningConnection = false;
    private Texture2D resoLinkConnectIcon;
    private Texture2D resoLinkDisconnectIcon;
    private Texture2D cacheConnectIcon;
    private Texture2D cacheDisconnectIcon;
    private Texture2D actionsIcon;
    private Texture2D lightingIcon;
    private ResoLinkHelper resoLinkHelper;
    private MeshXCache meshXCache;
    private GameObject selectedMeshObject;
    private GameObject selectedTextureObject;
    private LightingSettings lightingSettings;
    private Vector2 scrollPos;
    private string wsUrl = "ws://localhost:5000";

    private string[] denoiserOptions = new[] { "OIDN", "Optix" };
    private string[] denoiserOptionsAdvanced = new[] { "OIDN", "Optix", "None" };
    private string[] lightmapSizeOptions = new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096" };
    private string[] lightmapperOptions = new[] { "CPU", "GPU" };
    private string[] filteringOptions = new[] { "Auto", "Advanced", "None" };
    private string[] packingOptions = new[] { "Auto", "Custom" };
    private string[] packingMethodOptions = new[] { "Unity", "XAtlas" };
    private string[] directFilterOptions = new[] { "Gaussian", "A-Trous", "None" };

    private int directSamples = 32;
    private int indirectSamples = 512;
    private int environmentSamples = 256;
    private int maxBounces = 2;
    private int denoiserIndex = 0;
    private int lightmapSizeIndex = 5;
    private int lightmapResolution = 40;
    private bool enableAdvancedSettings = true;
    private int lightmapperIndex = 1;
    private bool importanceSampling = true;
    private int directFilterIndex = 1;
    private float directRadius = 1f;
    private float directSigma = 1f;
    private int indirectDenoiserIndex = 1;
    private int indirectFilterIndex = 1;
    private float indirectRadius = 1f;
    private float indirectSigma = 1f;
    private bool enableAO = false;
    private int aoDenoiserIndex = 2;
    private int aoFilterIndex = 1;
    private float aoRadius = 1f;
    private float aoSigma = 1f;
    private int packingIndex = 1;
    private int packingMethodIndex = 1;
    private int packingIterations = 16384;
    private int lightmapPadding = 2;
    private bool blockAlignedPacking = false;
    private float albedoBoost = 1f;
    private float indirectIntensity = 1f;
    private int filteringIndex = 1;

    [MenuItem("Tools/Light Baking")]
    public static void ShowWindow() {
        GetWindow<LightBaking>("Light Baking");
    }

    private void OnEnable() {
        resoLinkHelper = ResoLinkHelper.Instance;
        meshXCache = MeshXCache.Instance;
        resoLinkConnectIcon = EditorGUIUtility.IconContent("d_Linked@2x").image as Texture2D;
        resoLinkDisconnectIcon = EditorGUIUtility.IconContent("d_Unlinked@2x").image as Texture2D;
        cacheConnectIcon = EditorGUIUtility.IconContent("d_CacheServerConnected@2x").image as Texture2D;
        cacheDisconnectIcon = EditorGUIUtility.IconContent("d_CacheServerDisconnected@2x").image as Texture2D;
        actionsIcon = EditorGUIUtility.IconContent("d_Preset.Context@2x").image as Texture2D;
        lightingIcon = EditorGUIUtility.IconContent("d_LightingSettings Icon").image as Texture2D;
        cacheDirConnected = meshXCache.IsCacheDirConnected();
        dataDirConnected = meshXCache.IsDataDirConnected();
    }

    private void OnGUI() {
        if (Event.current.type == EventType.MouseDown) {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
        }

        if (resoLinkHelper == null) {
            resoLinkHelper = ResoLinkHelper.Instance;
        }

        if (meshXCache == null) {
            meshXCache = MeshXCache.Instance;
        }
     
        resoLinkConnected = resoLinkHelper.IsConnected();

        if (resoLinkConnected != meshXCache.isConnected && !resoLinkConnected) {
            EditorUtility.ClearProgressBar();
        }

        meshXCache.isConnected = resoLinkConnected;

        
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUIStyle.none, GUIStyle.none, GUILayout.ExpandHeight(true));
        CreateConnectionSettingsGUI();
        CreateCacheSettingsGUI();
        CreateActionsGUI();
        CreateTempGUI();
        CreateLightingGUI();
        EditorGUILayout.EndScrollView();
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

            string connectedText = resoLinkConnected ? "Disconnect" : "Connect";
            string transitioningText = resoLinkConnected ? "Disconnecting..." : "Connecting...";
            
            EditorGUI.BeginDisabledGroup(transitioningConnection || !(cacheDirConnected && dataDirConnected));

            if (GUILayout.Button(transitioningConnection ? transitioningText : connectedText)) {
                try {
                    transitioningConnection = true;
                    if (resoLinkConnected) {
                        resoLinkHelper.Disconnect();
                        EditorUtility.ClearProgressBar();
                    } else {
                        await resoLinkHelper.ConnectAsync(wsUrl);
                    }
                    Repaint();
                } finally {
                    transitioningConnection  = false;
                }
            }
            EditorGUI.indentLevel--;

            EditorGUI.EndDisabledGroup();
        }
    }

    private void CreateCacheSettingsGUI() {
        EditorGUILayout.BeginHorizontal();
        GUIContent content = new GUIContent("Cache Settings", cacheDirConnected && dataDirConnected ? cacheConnectIcon : cacheDisconnectIcon);
        showCacheSettings = EditorGUILayout.Foldout(showCacheSettings, content, true);
        EditorGUILayout.EndHorizontal();
        if (showCacheSettings) {
            EditorGUI.indentLevel++;
            CreateFolderPickerGUI("Cache Folder", cacheDirConnected, ref meshXCache.cacheDirectory);
            CreateFolderPickerGUI("Data Folder", dataDirConnected, ref meshXCache.dataDirectory);
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndDisabledGroup();
    }

    private void CreateActionsGUI() {
        EditorGUI.BeginDisabledGroup(!resoLinkConnected);
        EditorGUILayout.BeginHorizontal();
        GUIContent content = new GUIContent("ResoLink Actions", actionsIcon);
        showActionSettings = EditorGUILayout.Foldout(showActionSettings, content, true);
        EditorGUILayout.EndHorizontal();

        if (showActionSettings) {
            EditorGUI.indentLevel++;
            if (GUILayout.Button("Retrieve Resonite Data")) {
                RetrieveMesh();
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndDisabledGroup();
    }

    private void CreateTempGUI() {
        EditorGUILayout.BeginHorizontal();
        GUIContent content = new GUIContent("Temp Settings", lightingIcon);
        showTempSettings = EditorGUILayout.Foldout(showTempSettings, content, true);
        EditorGUILayout.EndHorizontal();

        if (showTempSettings) {
            EditorGUI.indentLevel++;

            GameObject newSelectedMeshObject = (GameObject)EditorGUILayout.ObjectField("Mesh Object", selectedMeshObject, typeof(GameObject), true);
            if (newSelectedMeshObject != selectedMeshObject) {
                selectedMeshObject = newSelectedMeshObject;
            }

            if (GUILayout.Button("Send Selected Mesh to ResoLink")) {
                GameObject meshObj = selectedMeshObject;
                ResoLinkHelper helper = resoLinkHelper;
                EditorApplication.delayCall += async () => {
                    if (meshObj != null && helper != null) {
                        MeshFilter meshFilter = meshObj.GetComponent<MeshFilter>();
                        if (await helper.SendUnityMeshToResoLink(meshFilter?.sharedMesh)) {
                            Debug.Log("Mesh sent successfully!");
                        } else {
                            Debug.LogError("Failed to send mesh.");
                        }
                    }
                };
            }

            GameObject newSelectedTextureObject = (GameObject)EditorGUILayout.ObjectField("Texture Object", selectedTextureObject, typeof(GameObject), true);
            if (newSelectedTextureObject != selectedTextureObject) {
                selectedTextureObject = newSelectedTextureObject;
            }

            if (GUILayout.Button("Send Selected Texture to ResoLink")) {
                GameObject texObj = selectedTextureObject;
                ResoLinkHelper helper = resoLinkHelper;
                EditorApplication.delayCall += async () => {
                    if (texObj != null && helper != null) {
                        if (await helper.SendTextureToResoLinkViaObject(texObj)) {
                            Debug.Log("Texture sent successfully!");
                        } else {
                            Debug.LogError("Failed to send texture.");
                        }
                    }
                };
            }

            EditorGUI.indentLevel--;
        }
    }

    private void CreateCustomPO2Slider(string label, int min, int max, ref int output) {
        int minExp = (int)Mathf.Log(min, 2);
        int maxExp = (int)Mathf.Log(max, 2);
        int currentExp = (int)Mathf.Log(Mathf.Clamp(output, min, max), 2);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(150));
        int sliderExp = (int)GUILayout.HorizontalSlider(currentExp, minExp, maxExp, GUILayout.ExpandWidth(true));
        GUILayout.Space(-10);
        string tempString = EditorGUILayout.TextField(output.ToString(), GUILayout.ExpandWidth(false), GUILayout.Width(75));
        EditorGUILayout.EndHorizontal();

        int newValue = output;
        if (sliderExp != currentExp) {
            newValue = (int)Mathf.Pow(2, sliderExp);
        } else if (int.TryParse(tempString, out int parsedInt)) {
            newValue = Mathf.ClosestPowerOfTwo(parsedInt);
        }

        output = Mathf.Clamp(newValue, min, max);
    }

    private void CreateFilterGUIOption(int index, ref float radius, ref float sigma) {
        EditorGUI.indentLevel++;

        switch (index) {
            case 0:
                radius = EditorGUILayout.Slider("Radius", radius, 0f, 5f);
                break;
            case 1:
                sigma = EditorGUILayout.Slider("Sigma", sigma, 0f, 10f);
                break;
            default:
                break;
        }

        EditorGUI.indentLevel--;
    }

    private void CreateLightingGUI() {
        EditorGUI.BeginDisabledGroup(Lightmapping.isRunning);

        EditorGUILayout.BeginHorizontal();
        showLightingSettings = EditorGUILayout.Foldout(showLightingSettings, new GUIContent("Lighting Settings", lightingIcon), true);
        EditorGUILayout.EndHorizontal();

        if (showLightingSettings) {
            EditorGUI.indentLevel++;

            if (enableAdvancedSettings) {
                lightmapperIndex = EditorGUILayout.Popup("Lightmapper", lightmapperIndex, lightmapperOptions);
                importanceSampling = EditorGUILayout.Toggle("Importance Sampling", importanceSampling);
            }

            CreateCustomPO2Slider("Direct Samples", 1, 1024, ref directSamples);
            CreateCustomPO2Slider("Indirect Samples", 1, 8192, ref indirectSamples);
            CreateCustomPO2Slider("Environment Samples", 1, 2048, ref environmentSamples);

            maxBounces = EditorGUILayout.IntField("Max Bounces", maxBounces);

            if (enableAdvancedSettings) {
                filteringIndex = EditorGUILayout.Popup("Filtering", filteringIndex, filteringOptions);
                EditorGUI.indentLevel++;
            }

            string[] currentDenoiserOptions = enableAdvancedSettings ? denoiserOptionsAdvanced : denoiserOptions;
            
            if (filteringIndex == 1) {
                denoiserIndex = EditorGUILayout.Popup("Denoiser", denoiserIndex, currentDenoiserOptions);

                if (enableAdvancedSettings) {
                    directFilterIndex = EditorGUILayout.Popup("Direct Filter", directFilterIndex, directFilterOptions);
                    CreateFilterGUIOption(directFilterIndex, ref directRadius, ref directSigma);


                    indirectDenoiserIndex = EditorGUILayout.Popup("Indirect Denoiser", indirectDenoiserIndex, denoiserOptionsAdvanced);
                    indirectFilterIndex = EditorGUILayout.Popup("Indirect Filter", indirectFilterIndex, directFilterOptions);
                    CreateFilterGUIOption(indirectFilterIndex, ref indirectRadius, ref indirectSigma);

                    if (enableAO) {                    
                        aoDenoiserIndex = EditorGUILayout.Popup("AO Denoiser", aoDenoiserIndex, denoiserOptionsAdvanced);
                        aoFilterIndex = EditorGUILayout.Popup("AO Filter", aoFilterIndex, directFilterOptions);
                        CreateFilterGUIOption(aoFilterIndex, ref aoRadius, ref aoSigma);
                    }

                    EditorGUI.indentLevel--;
                }
            }

            if (enableAdvancedSettings) {
                packingIndex = EditorGUILayout.Popup("Lightmap Packing", packingIndex, packingOptions);

                if (packingIndex == 1) {
                    EditorGUI.indentLevel++;
                    packingMethodIndex = EditorGUILayout.Popup("Packing Method", packingMethodIndex, packingMethodOptions);
                    packingIterations = EditorGUILayout.IntSlider("Packing Iterations", packingIterations, 1, 10);
                    lightmapPadding = EditorGUILayout.IntField("Lightmap Padding", lightmapPadding);
                    blockAlignedPacking = EditorGUILayout.Toggle("Block Aligned", blockAlignedPacking);
                    EditorGUI.indentLevel--;
                }
            }

            lightmapResolution = EditorGUILayout.IntField("Lightmap Resolution", lightmapResolution);
            lightmapSizeIndex = EditorGUILayout.Popup("Max Lightmap Size", lightmapSizeIndex, lightmapSizeOptions);

            if (enableAdvancedSettings) {
                enableAO = EditorGUILayout.Toggle("Ambient Occlusion", enableAO);
                albedoBoost = EditorGUILayout.Slider("Albedo Boost", albedoBoost, 0f, 10f);
                indirectIntensity = EditorGUILayout.Slider("Indirect Intensity", indirectIntensity, 0f, 5f);
            }

            bool oldAdvancedSettings = enableAdvancedSettings;
            enableAdvancedSettings = EditorGUILayout.Toggle("Advanced Settings", enableAdvancedSettings);

            if (oldAdvancedSettings != enableAdvancedSettings && filteringIndex == 1) {
                currentDenoiserOptions = enableAdvancedSettings ? denoiserOptionsAdvanced : denoiserOptions;
                denoiserIndex = Math.Clamp(denoiserIndex, 0, currentDenoiserOptions.Length - 1);
            }

            if (GUILayout.Button("Bake Lighting")) {
                UpdateLightingSettings();
                Lightmapping.lightingSettings = lightingSettings;
                if (resoLinkConnected && Lightmapping.BakeAsync()) { 
                    Debug.Log("Bake Lighting triggered!");
                    Lightmapping.bakeCompleted += OnBakeComplete;
                }
            }

            EditorGUI.indentLevel--;
        }
        EditorGUI.EndDisabledGroup();
    }

    private async void OnBakeComplete() {
        Lightmapping.bakeCompleted -= OnBakeComplete;

        foreach (LightmapData lm in LightmapSettings.lightmaps) {
            if (lm.lightmapColor != null) {
                await resoLinkHelper.SendTextureToResoLink(lm.lightmapColor);
            }
        }
    }

    private void UpdateLightingSettings() {
        lightingSettings = new LightingSettings();

        lightingSettings.lightmapper = new[] {
            LightingSettings.Lightmapper.ProgressiveCPU,
            LightingSettings.Lightmapper.ProgressiveGPU
        }[lightmapperIndex];

        lightingSettings.environmentImportanceSampling = importanceSampling;
        lightingSettings.directSampleCount = directSamples;
        lightingSettings.indirectSampleCount = indirectSamples;
        lightingSettings.environmentSampleCount = environmentSamples;
        lightingSettings.maxBounces = maxBounces;
        lightingSettings.filteringMode = new[] {
            LightingSettings.FilterMode.Auto,
            LightingSettings.FilterMode.Advanced,
            LightingSettings.FilterMode.None
        }[filteringIndex];
        lightingSettings.denoiserTypeDirect = new[] {
            LightingSettings.DenoiserType.OpenImage,
            LightingSettings.DenoiserType.Optix,
            LightingSettings.DenoiserType.None
        }[denoiserIndex];
        lightingSettings.filterTypeDirect = new[] {
            LightingSettings.FilterType.Gaussian,
            LightingSettings.FilterType.ATrous,
            LightingSettings.FilterType.None
        }[directFilterIndex];
        lightingSettings.filteringGaussianRadiusDirect = directRadius;
        lightingSettings.filteringAtrousPositionSigmaDirect = directSigma;
        lightingSettings.filterTypeIndirect = new[] {
            LightingSettings.FilterType.Gaussian,
            LightingSettings.FilterType.ATrous,
            LightingSettings.FilterType.None
        }[indirectFilterIndex];
        lightingSettings.filteringGaussianRadiusDirect = indirectRadius;
        lightingSettings.filteringAtrousPositionSigmaDirect = indirectSigma;
        lightingSettings.filterTypeAO = new[] {
            LightingSettings.FilterType.Gaussian,
            LightingSettings.FilterType.ATrous,
            LightingSettings.FilterType.None
        }[aoFilterIndex];
        lightingSettings.filteringGaussianRadiusDirect = aoRadius;
        lightingSettings.filteringAtrousPositionSigmaDirect = aoSigma;
        lightingSettings.lightmapPackingMode = new[] {
            LightingSettings.LightmapPackingMode.Auto,
            LightingSettings.LightmapPackingMode.Custom
        }[packingIndex];
        lightingSettings.lightmapPackingMethod = new[] {
            LightingSettings.LightmapPackingMethod.Unity,
            LightingSettings.LightmapPackingMethod.XAtlas
        }[packingMethodIndex];
        lightingSettings.packingAttempts = packingIterations;
        lightingSettings.lightmapPadding = lightmapPadding;
        lightingSettings.blockAlignedPacking = blockAlignedPacking;
        lightingSettings.lightmapResolution = lightmapResolution;
        lightingSettings.ao = enableAO;
        lightingSettings.albedoBoost = albedoBoost;
        lightingSettings.aoExponentIndirect = indirectIntensity;
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
                cacheDirConnected = meshXCache.IsCacheDirConnected();
                dataDirConnected = meshXCache.IsDataDirConnected();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private Task ProgressBar(Func<Action<string, float>, Task> func) {
        return func((message, progress) => {
            EditorUtility.DisplayProgressBar("Light Baking", message, Mathf.Clamp01(progress));
        });
    }

    private async void RetrieveMesh() {
        try {
            Debug.Log("Starting Retrieval from ResoLink");
            await Task.Delay(1);
            await ProgressBar(meshXCache.UpdatePathCache);
            await Task.Delay(1);
            await ProgressBar(resoLinkHelper.FetchMeshSlots);
            await Task.Delay(1);
            await ProgressBar(resoLinkHelper.BuildLookupTables);
            await Task.Delay(1);
            await ProgressBar(resoLinkHelper.ApplyTRSToObjects);
            await Task.Delay(1);
            await resoLinkHelper.DownloadAndApplyMeshes((message, progress, obj) => {
                EditorUtility.DisplayProgressBar("Light Baking", message, Mathf.Clamp01(progress));
                if (obj != null) {
                    FocusNewObject(obj);
                }
            });
        } catch (Exception e) {
            Debug.LogError($"Error during mesh retrieval: {e.Message}\n{e.StackTrace}");
        } finally {
            EditorUtility.ClearProgressBar();
            Debug.Log("Retrieval from ResoLink finished");
        }
    }

    private void FocusNewObject(GameObject obj) {
        Selection.activeGameObject = obj;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
}